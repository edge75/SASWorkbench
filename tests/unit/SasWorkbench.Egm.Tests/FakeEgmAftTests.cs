using System.Diagnostics;
using System.Text;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's funds transfer, against the machine library's lp72_TransferFunds and
/// lp74_LockAndStatusRequest: the lock and the ways it ends, a transfer that goes pending and is
/// read back final, the history and its wrap, the two-byte refusals, the transfer exceptions'
/// place in the queue, and the collect button with cash-out to the host on and off. Every case
/// runs with the host in a sequence, so nothing reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmAftTests : IDisposable
{
    private const uint Asset = 12345;
    private const byte GameLocked = 0x6F;
    private const byte TransferComplete = 0x69;
    private const byte HostCashoutRequest = 0x6A;

    private static readonly SasMessageLayout LockLayout = SasLayouts.For(0x74)!;
    private static readonly SasMessageLayout TransferLayout = SasLayouts.For(0x72)!;

    private readonly LoopbackBus _bus = new();
    private readonly string _wallet = Path.Combine(Path.GetTempPath(), $"aft-wallet-{Guid.NewGuid():N}");
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(FakeEgmOptions? options = null)
    {
        _machine = new FakeEgm(_bus.Egm, (options ?? new FakeEgmOptions()) with { AssetNumber = Asset });
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromSeconds(1),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
        if (Directory.Exists(_wallet))
            Directory.Delete(_wallet, recursive: true);
    }

    private SasDecoded Lock(string request)
    {
        LongPollResult result = Host.Execute(0x74, Convert.FromHexString(request));
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(LockLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private long LockStatus(string request) => Lock(request).Number("game_lock_status")!.Value;

    private SasDecoded Send72(byte[] data)
    {
        LongPollResult result = Host.Execute(0x72, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(TransferLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private SasDecoded Interrogate(byte index = 0) => Send72([0x02, 0xFF, index]);

    /// <summary>A full or partial transfer request, laid out at the library's fixed offsets.</summary>
    private static byte[] Transfer(byte type, long cents, string id, byte code = 0x00, byte flags = 0x00, uint asset = Asset)
    {
        var data = new List<byte> { 0x00, code, 0x00, type };
        data.AddRange(Bcd(cents, 5));
        data.AddRange(new byte[10]);
        data.Add(flags);
        data.AddRange([(byte)asset, (byte)(asset >> 8), (byte)(asset >> 16), (byte)(asset >> 24)]);
        data.AddRange(new byte[20]);
        data.Add((byte)id.Length);
        data.AddRange(Encoding.ASCII.GetBytes(id));
        data.AddRange(new byte[4 + 2 + 1 + 2]);
        data[0] = (byte)(data.Count - 1);
        return [.. data];
    }

    private static byte[] Bcd(long value, int bytes)
    {
        byte[] digits = new byte[bytes];
        for (int i = bytes - 1; i >= 0; i--)
        {
            digits[i] = (byte)((value % 10) | (((value / 10) % 10) << 4));
            value /= 100;
        }

        return digits;
    }

    /// <summary>Interrogates until the transfer is final, which the platform reaches after its completion delay.</summary>
    private SasDecoded WaitForFinal()
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            SasDecoded decoded = Send72([0x02, 0xFE, 0x00]);
            if (decoded.Number("transfer_status") != 0x40)
                return Interrogate();
            Thread.Sleep(20);
        }

        throw new TimeoutException("the transfer never became final");
    }

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>Drains everything queued, acknowledging each with a long poll in between, and returns the codes in order.</summary>
    private List<byte> Drain()
    {
        var codes = new List<byte>();
        Host.Synchronise();
        for (byte code = Poll(); code != 0x00; code = Poll())
        {
            codes.Add(code);
            Host.Synchronise();
        }

        return codes;
    }

    private static FakeEgmOptions Quick => new() { AftCompletionDelay = TimeSpan.Zero };

    [Fact]
    public void A_lock_is_taken_at_once_reported_by_game_locked_and_cancelled()
    {
        Start();

        Assert.Equal(0x00, LockStatus("000F9999"));
        Assert.Equal(0x00, LockStatus("FF000000"));
        Assert.Equal(0x00, Machine.AftLockStatus);
        Assert.Contains(GameLocked, Drain());

        Assert.Equal(0xFF, LockStatus("80000000"));
        Assert.Equal(0xFF, LockStatus("FF000000"));
    }

    [Fact]
    public void A_lock_runs_out_by_itself_after_its_timeout()
    {
        Start();

        Assert.Equal(0x00, LockStatus("000F0030"));
        Thread.Sleep(600);
        Assert.Equal(0xFF, LockStatus("FF000000"));
    }

    /// <summary>
    /// The library takes a lock with no timeout - the condition is met - and the Windows timer it
    /// arms with a zero due time fires at once, so the lock ends as soon as it begins.
    /// </summary>
    [Fact]
    public void A_lock_with_no_timeout_is_answered_locked_and_ends_at_once()
    {
        Start();

        Assert.Equal(0x00, LockStatus("000F0000"));
        Thread.Sleep(100);
        Assert.Equal(0xFF, LockStatus("FF000000"));
    }

    [Fact]
    public void A_lock_naming_no_transfer_condition_is_refused()
    {
        Start();

        Assert.Equal(0xFF, LockStatus("00000000"));
        Assert.Equal(0xFF, LockStatus("FF000000"));
    }

    [Fact]
    public void Opening_a_door_ends_the_lock_and_offers_no_transfers()
    {
        Start();
        Assert.Equal(0x00, LockStatus("000F9999"));

        Machine.SetDoor("main", open: true);

        SasDecoded reading = Lock("FF000000");
        Assert.Equal(0xFF, reading.Number("game_lock_status"));
        Assert.Equal(0x00, reading.Number("available_transfers"));
        Assert.Equal(0xFF, LockStatus("000F9999"));
    }

    [Fact]
    public void A_loop_break_ends_the_lock()
    {
        Start(new FakeEgmOptions { LinkLossAfter = TimeSpan.FromSeconds(1.5) });
        Assert.Equal(0x00, LockStatus("000F9999"));

        Thread.Sleep(2000);
        Host.Synchronise();

        Assert.Equal(0xFF, LockStatus("FF000000"));
    }

    [Fact]
    public void A_transfer_in_is_pending_then_final_and_moves_its_meters()
    {
        Start();
        long before = Machine.Meters.Read(SasMeterCode.CurrentCredits);

        SasDecoded pending = Send72(Transfer(0x00, 500, "2"));
        Assert.Equal(0x40, pending.Number("transfer_status"));
        Assert.Equal(0xFF, pending.Number("receipt_status"));
        Assert.Equal(500, pending.Number("cashable_amount"));
        Assert.Equal(0, pending.Number("transaction_date"));
        Assert.Equal(0, pending.Number("cashable_meter_size"));

        // Read back at once: the platform has not finished, so the same pending record.
        Assert.Equal(0x40, Interrogate().Number("transfer_status"));

        SasDecoded final = WaitForFinal();
        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(1, final.Number("transaction_buffer_position"));
        Assert.Equal(500, final.Number("cashable_amount"));
        Assert.Equal(500, final.Number("cashable_meter"));
        Assert.Equal(5, final.Number("cashable_meter_size"));
        Assert.NotEqual(0, final.Number("transaction_date"));

        Assert.Equal(before + (500 / Machine.AccountingCents), Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.InHouseCashableTransfersToGamingMachine_Cents));
        Assert.Equal(1, Machine.Meters.Read(SasMeterCode.InHouseTransfersToGamingMachineThatIncludedCashableAmounts_Qty));
        Assert.Equal(500, Lock("FF000000").Number("current_cashable_amount"));
    }

    [Fact]
    public void A_transfer_out_takes_the_credits_to_the_host()
    {
        Start(Quick);
        Send72(Transfer(0x00, 1000, "1"));
        WaitForFinal();

        Assert.Equal(0x40, Send72(Transfer(0x80, 1000, "2")).Number("transfer_status"));
        SasDecoded final = WaitForFinal();

        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(2, final.Number("transaction_buffer_position"));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(1000, Machine.Meters.Read(SasMeterCode.InHouseCashableTransfersToHost_Cents));
    }

    [Fact]
    public void A_transfer_while_one_is_pending_is_answered_c0_in_two_bytes()
    {
        Start();
        Send72(Transfer(0x00, 500, "1"));

        SasDecoded refused = Send72(Transfer(0x00, 500, "2"));

        Assert.Equal(2, refused.Number("length"));
        Assert.Equal(0, refused.Number("transaction_buffer_position"));
        Assert.Equal(0xC0, refused.Number("transfer_status"));
    }

    [Fact]
    public void An_unsupported_transfer_code_is_answered_c1_in_two_bytes()
    {
        Start();

        SasDecoded refused = Send72([0x02, 0x05, 0x00]);

        Assert.Equal(2, refused.Number("length"));
        Assert.Equal(0xC1, refused.Number("transfer_status"));
    }

    [Fact]
    public void Refusals_carry_the_library_code_and_take_no_place_in_the_history()
    {
        Start(Quick);

        Assert.Equal(0x93, Send72(Transfer(0x00, 500, "1", asset: 1)).Number("transfer_status"));
        Assert.Equal(0x94, Send72(Transfer(0x00, 500, "2", flags: 0x40)).Number("transfer_status"));
        Assert.Equal(0x85, Send72(Transfer(0x00, 501, "3")).Number("transfer_status"));
        Assert.Equal(0x84, Send72(Transfer(0x00, 2_000_000, "4")).Number("transfer_status"));
        Assert.Equal(0x88, Send72(Transfer(0x40, 500, "5")).Number("transfer_status"));

        SasDecoded read = Interrogate();
        Assert.Equal(0x88, read.Number("transfer_status"));
        Assert.Equal(0, read.Number("transaction_buffer_position"));
        Assert.Equal(1, Machine.AftHistoryIndex);
    }

    [Fact]
    public void The_same_identifier_twice_is_not_unique()
    {
        Start(Quick);
        Send72(Transfer(0x00, 500, "20"));
        WaitForFinal();

        Assert.Equal(0x81, Send72(Transfer(0x00, 500, "20")).Number("transfer_status"));
    }

    [Fact]
    public void An_interrogation_with_no_history_answers_three_bytes()
    {
        Start();

        SasDecoded read = Interrogate();

        Assert.Equal(3, read.Number("length"));
        Assert.Equal(0xFF, read.Number("transfer_status"));
        Assert.Equal(0xFF, read.Number("receipt_status"));
    }

    [Fact]
    public void The_history_fills_its_127_positions_and_wraps_to_the_first()
    {
        Start(Quick);

        for (int n = 1; n <= 128; n++)
        {
            byte type = n % 2 == 1 ? (byte)0x00 : (byte)0x80;
            Send72(Transfer(type, Machine.AccountingCents, n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            SasDecoded final = WaitForFinal();
            Assert.Equal(0x00, final.Number("transfer_status"));
            Assert.Equal(n <= 127 ? n : 1, final.Number("transaction_buffer_position"));
        }

        Assert.Equal("128", Interrogate(1)["transaction_id"]!.Text);
        Assert.Equal("127", Interrogate(127)["transaction_id"]!.Text);
    }

    [Fact]
    public void Transfer_complete_goes_ahead_of_the_queue_and_stops_when_read()
    {
        Start(Quick);
        Machine.SetDoor("main", open: true);
        Machine.SetDoor("main", open: false);

        Send72(Transfer(0x00, 500, "1"));
        Thread.Sleep(100);

        Host.Synchronise();
        Assert.Equal(TransferComplete, Poll());

        WaitForFinal();
        Assert.DoesNotContain(TransferComplete, Drain());
    }

    [Fact]
    public void Game_locked_goes_ahead_of_the_queue()
    {
        Start();
        Machine.SetDoor("main", open: true);
        Machine.SetDoor("main", open: false);

        Assert.Equal(0x00, LockStatus("000F9999"));

        Host.Synchronise();
        Assert.Equal(GameLocked, Poll());
    }

    [Fact]
    public void With_cashout_to_the_host_off_the_button_pays_a_ticket()
    {
        Start(Quick);
        Send72(Transfer(0x00, 500, "1"));
        WaitForFinal();
        Drain();

        Assert.NotNull(Machine.Cashout(_wallet));
        Assert.DoesNotContain(HostCashoutRequest, Drain());
    }

    [Fact]
    public void With_cashout_to_the_host_on_the_button_asks_and_the_host_takes_the_credits()
    {
        Start(Quick);

        // Flags 0 and 1: the host takes control of cash-out and turns it on, applied once the transfer succeeds.
        Send72(Transfer(0x00, 500, "1", flags: 0x03));
        WaitForFinal();
        Assert.Equal(0x03, Lock("FF000000").Number("host_cashout_status"));
        Drain();

        Assert.Null(Machine.Cashout(_wallet));
        Assert.True(Machine.HostCashoutRequested);

        // $6A ranks ahead of the button's own $66, which is an ordinary exception.
        Host.Synchronise();
        Assert.Equal(HostCashoutRequest, Poll());

        Send72(Transfer(0x80, 500, "2", code: 0x01));
        Assert.Equal(0x00, WaitForFinal().Number("transfer_status"));
        Assert.False(Machine.HostCashoutRequested);
        List<byte> after = Drain();
        Assert.Contains((byte)0x66, after);
        Assert.DoesNotContain(HostCashoutRequest, after);
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.False(Directory.Exists(_wallet) && Directory.EnumerateFiles(_wallet).Any());
    }

    /// <summary>Eight seconds with no answer from the host, and the machine pays the usual way.</summary>
    [Fact]
    public void An_unanswered_cashout_request_ends_with_a_ticket_after_eight_seconds()
    {
        Start(Quick);
        Send72(Transfer(0x00, 500, "1", flags: 0x03));
        WaitForFinal();

        Assert.Null(Machine.Cashout(_wallet));
        Thread.Sleep(TimeSpan.FromSeconds(9));

        Assert.False(Machine.HostCashoutRequested);
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Single(Directory.EnumerateFiles(_wallet));
    }

    /// <summary>A zero transfer to the host answers a cashout request, and the machine then pays the usual way.</summary>
    [Fact]
    public void A_zero_transfer_to_the_host_ends_the_request_and_the_machine_pays()
    {
        Start(Quick);
        Send72(Transfer(0x00, 500, "1", flags: 0x03));
        WaitForFinal();
        Assert.Null(Machine.Cashout(_wallet));

        SasDecoded zero = Send72(Transfer(0x80, 0, "4"));
        Assert.Equal(0x40, zero.Number("transfer_status"));
        Thread.Sleep(200);
        Host.Synchronise();
        Assert.Equal(TransferComplete, Poll());
        SasDecoded final = WaitForFinal();

        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(0, final.Number("transaction_buffer_position"));
        Assert.False(Machine.HostCashoutRequested);
        Assert.Single(Directory.EnumerateFiles(_wallet));
    }
}
