using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's validation styles, against the machine library's lp4C, lp4D, lp57, lp58
/// and lp7B handlers, sasStartCashout, internalCompleteCashout and the validation buffer, with the
/// platform's printing rules as QSP has them. The host runs in a sequence, so nothing reaches the
/// line the test did not send.
/// </summary>
public sealed partial class FakeEgmValidationTests : IDisposable
{
    private const byte CashoutTicketPrinted = 0x3D;
    private const byte ValidationIdNotConfigured = 0x3F;
    private const byte HandpayIsPending = 0x51;
    private const byte SystemValidationRequest = 0x57;

    private static readonly SasMessageLayout FeaturesLayout = SasLayouts.For(0xA0)!;
    private static readonly SasMessageLayout IdLayout = SasLayouts.For(0x4C)!;
    private static readonly SasMessageLayout RecordLayout = SasLayouts.For(0x4D)!;
    private static readonly SasMessageLayout PendingLayout = SasLayouts.For(0x57)!;
    private static readonly SasMessageLayout NumberLayout = SasLayouts.For(0x58)!;
    private static readonly SasMessageLayout StatusLayout = SasLayouts.For(0x7B)!;

    private readonly LoopbackBus _bus = new();
    private readonly string _wallet = Path.Combine(Path.GetTempPath(), "sasworkbench-validation-" + Guid.NewGuid().ToString("N"));
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(SasCashoutValidation style, Func<ISasTransport, ISasTransport>? line = null)
    {
        ISasTransport egm = line is null ? _bus.Egm : line(_bus.Egm);
        _machine = new FakeEgm(egm, new FakeEgmOptions { CashoutValidation = style });
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

    private SasDecoded Ask(SasMessageLayout layout, byte[] data)
    {
        LongPollResult result = Host.Execute(layout.Command, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    private static byte[] Id(uint identifier, uint sequence) =>
    [
        (byte)identifier, (byte)(identifier >> 8), (byte)(identifier >> 16),
        (byte)sequence, (byte)(sequence >> 8), (byte)(sequence >> 16),
    ];

    private SasDecoded SetId(uint identifier, uint sequence) => Ask(IdLayout, Id(identifier, sequence));

    private SasDecoded Record(byte function) => Ask(RecordLayout, [function]);

    /// <summary>A $4D read and the long poll that acknowledges it, as the host's next message does.</summary>
    private SasDecoded ReadAndAcknowledge()
    {
        SasDecoded record = Record(0x00);
        Host.Synchronise();
        return record;
    }

    private SasDecoded Status(ushort mask, ushort states) =>
        Ask(StatusLayout, [0x08, (byte)mask, (byte)(mask >> 8), (byte)states, (byte)(states >> 8), 0, 0, 0, 0]);

    private Ticket? CashOut(int cents = 2000)
    {
        Machine.InsertBill(cents);
        return Machine.Cashout(_wallet);
    }

    private byte Poll()
    {
        GeneralPollResult result = Host.PollGenerally();
        Assert.True(result.Answered, $"the general poll went unanswered: {result.Outcome}");
        return result.Code;
    }

    /// <summary>
    /// Waits for work the machine does after its reply has gone out, as the library hands the print or
    /// the hand pay to the platform only once $58 is answered.
    /// </summary>
    private static bool Eventually(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= until)
                return false;
            Thread.Sleep(10);
        }

        return true;
    }

    private int WalletFiles() => Directory.Exists(_wallet) ? Directory.GetFiles(_wallet).Length : 0;

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

    /// <summary>Drains until the code is heard, within its fifteen-second reissue, for a loaded machine whose first drain may come up short.</summary>
    private bool Heard(byte code)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        do
        {
            if (Drain().Contains(code))
                return true;
        }
        while (DateTime.UtcNow < until);

        return false;
    }

    [Fact]
    public void A_secure_enhanced_machine_reports_its_style_and_the_extensions_bit()
    {
        Start(SasCashoutValidation.SecureEnhanced);

        SasDecoded features = Ask(FeaturesLayout, [0x00, 0x00]);

        Assert.Equal(2, features.Number("validation_style"));
        Assert.Equal(1, features.Number("validation_extensions"));
        Assert.Equal(1, features.Number("ticket_redemption"));
    }

    [Fact]
    public void Without_an_identifier_3F_is_reported_at_power_up_and_a_cash_out_is_paid_by_hand()
    {
        // sasInit queues 3F when the identifier is unset (libsas.cpp:1999), and sasStartCashout then
        // refuses, which QSP turns into a hand pay (SasNetwork_Ticket.hpp:1040).
        Start(SasCashoutValidation.SecureEnhanced);

        Assert.True(Heard(ValidationIdNotConfigured));

        Assert.Null(CashOut());
        Assert.True(Machine.HandpayPending);
        Assert.True(Heard(HandpayIsPending));
    }

    [Fact]
    public void The_identifier_rules_follow_the_library()
    {
        // lp.cpp:2479: the same identifier and base do not rewind a machine that has spent from them,
        // a zero identifier only asks, and a new base is taken.
        Start(SasCashoutValidation.SecureEnhanced);

        SasDecoded set = SetId(0x100001, 1);
        Assert.Equal(0x100001, set.Number("validation_id"));
        Assert.Equal(1, set.Number("sequence_number"));

        Assert.NotNull(CashOut());
        Assert.Equal(2, SetId(0, 0).Number("sequence_number"));
        Assert.Equal(2, SetId(0x100001, 1).Number("sequence_number"));
        Assert.Equal(0xFFFFFF, SetId(0x100001, 0xFFFFFF).Number("sequence_number"));

        Assert.NotNull(CashOut());
        Assert.Equal(0, SetId(0, 0).Number("sequence_number"));
    }

    [Fact]
    public void The_validation_number_is_the_library_algorithm()
    {
        // Identifier $100001 at sequence 2 makes 0230633823748784 (internalCalculateSecureEnhancedValidationId).
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(0x100001, 1);

        Ticket ticket = CashOut()!;
        SasDecoded record = ReadAndAcknowledge();

        Assert.Equal(230633823748784UL, ticket.ValidationNumber);
        Assert.Equal((long)ticket.ValidationNumber, record.Number("validation_number"));
        Assert.Equal(0, record.Number("validation_system_id"));
        Assert.Equal(2000, record.Number("amount"));
        Assert.Equal(ticket.TicketNumber, (uint)record.Number("ticket_number")!.Value);
        Assert.Equal(1, record.Number("index"));
        Assert.Equal(30, record.Number("expiration_year"));
    }

    [Fact]
    public void A_record_is_retired_only_by_an_acknowledged_read_of_function_zero()
    {
        // FF looks without moving (lp.cpp:2593); 00 queues the retirement behind the implied ACK (lp.cpp:2790).
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(1, 1);
        Ticket ticket = CashOut()!;

        Assert.Equal((long)ticket.ValidationNumber, Record(0xFF).Number("validation_number"));
        Host.Synchronise();
        Assert.Equal(1, Machine.UnreadValidationRecords);

        Assert.Equal((long)ticket.ValidationNumber, ReadAndAcknowledge().Number("validation_number"));
        Assert.Equal(0, Machine.UnreadValidationRecords);

        SasDecoded empty = Record(0x00);
        Assert.Equal(0, empty.Number("validation_number"));
        Assert.Equal(0, empty.Number("amount"));
        Assert.Equal(0, empty.Number("index"));
    }

    [Fact]
    public void The_same_read_sent_again_is_answered_again_and_retires_one_record()
    {
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(1, 1);
        Ticket first = CashOut()!;
        CashOut();

        Assert.Equal((long)first.ValidationNumber, Record(0x00).Number("validation_number"));
        Assert.Equal((long)first.ValidationNumber, Record(0x00).Number("validation_number"));
        Host.Synchronise();

        Assert.Equal(1, Machine.UnreadValidationRecords);
    }

    [Fact]
    public void The_buffer_wraps_to_slot_one_and_the_oldest_record_is_lost()
    {
        // sasStartCashout wraps the write index from 31 back to the buffer start, 1 (libsas_wrapper.cpp:2263).
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(1, 1);

        Ticket? second = null, last = null;
        for (int i = 1; i <= 32; i++)
        {
            Ticket ticket = CashOut()!;
            ReadAndAcknowledge();
            if (i == 2)
                second = ticket;
            last = ticket;
        }

        Assert.Equal((long)last!.ValidationNumber, Record(0x01).Number("validation_number"));
        Assert.Equal((long)second!.ValidationNumber, Record(0x02).Number("validation_number"));
        Assert.Equal(0, Record(0x20).Number("validation_number"));
    }

    [Fact]
    public void A_secure_enhanced_machine_leaves_3D_unanswered()
    {
        // lp.cpp:2143, section 15.5.
        Start(SasCashoutValidation.SecureEnhanced);

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x3D, []).Outcome);
    }

    [Fact]
    public void A_standard_machine_answers_neither_4C_nor_4D()
    {
        Start(SasCashoutValidation.Standard);

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x4C, Id(1, 1)).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x4D, [0x00]).Outcome);
    }

    [Fact]
    public void The_status_poll_reads_the_library_and_platform_words_and_bit_0_withdraws_the_printer()
    {
        // The host's word starts all allowed and QSP clears handpay receipts and validation (SasNetwork.cpp:617).
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(1, 1);

        Assert.Equal(0x8021, Status(0, 0).Number("current_validation_status"));
        Assert.Equal(0x8021, Status(0x0004, 0x0004).Number("current_validation_status"));
        Assert.Equal(0x8020, Status(0x0001, 0x0000).Number("current_validation_status"));

        // QSP pays by hand while the host has withdrawn the printer (CashoutPrinterRules.h:73).
        Assert.Null(CashOut());
        Assert.True(Machine.HandpayPending);
    }

    [Fact]
    public void Clearing_bit_15_cancels_the_identifier_and_3F_is_reported()
    {
        // lp.cpp:7232.
        Start(SasCashoutValidation.SecureEnhanced);
        SetId(1, 1);
        Drain();

        Status(0x8000, 0x0000);

        Assert.Contains(ValidationIdNotConfigured, Drain());
        Assert.Null(CashOut());
        Assert.True(Machine.HandpayPending);
    }

    [Fact]
    public void A_system_cash_out_waits_for_the_host_number_and_prints_it()
    {
        // sasStartCashout queues 57 (libsas_wrapper.cpp:2327); lp57 moves it on, lp58 grants it.
        Start(SasCashoutValidation.System);

        Assert.Null(CashOut());
        Assert.Empty(Directory.Exists(_wallet) ? Directory.GetFiles(_wallet) : []);
        // Reissued every 800 ms until $57 is read, so one poll is enough to hear it.
        Host.Synchronise();
        Assert.Equal(SystemValidationRequest, Poll());
        Host.Synchronise();

        SasDecoded pending = Ask(PendingLayout, []);
        Assert.Equal(0x00, pending.Number("cashout_type"));
        Assert.Equal(2000, pending.Number("amount"));

        SasDecoded answered = Ask(NumberLayout, [0x07, 0x00, 0x00, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78]);
        Assert.Equal(0x00, answered.Number("status"));

        Assert.True(Eventually(() => WalletFiles() == 1), "the granted ticket was never printed");
        Assert.Contains(CashoutTicketPrinted, Drain());
        SasDecoded record = ReadAndAcknowledge();
        Assert.Equal(12345678, record.Number("validation_number"));
        Assert.Equal(7, record.Number("validation_system_id"));
    }

    [Fact]
    public void A_number_without_57_first_is_rejected_and_the_cash_out_is_paid_by_hand()
    {
        // internalResolveValidationNumberStatus answers 81, and the denial cancels the cash-out (lp.cpp:3651).
        Start(SasCashoutValidation.System);
        CashOut();

        SasDecoded answered = Ask(NumberLayout, [0x07, 0x00, 0x00, 0x00, 0x00, 0x12, 0x34, 0x56, 0x78]);

        Assert.Equal(0x81, answered.Number("status"));
        Assert.True(Eventually(() => Machine.HandpayPending), "the denied cash-out was never paid by hand");
        Assert.Equal(0, Machine.UnreadValidationRecords);
    }

    [Fact]
    public void A_number_with_no_cash_out_is_answered_80()
    {
        Start(SasCashoutValidation.System);

        Assert.Equal(0x80, Ask(NumberLayout, [0x07, 0, 0, 0, 0, 0, 0, 0, 1]).Number("status"));
        Assert.Equal(0x80, Ask(PendingLayout, []).Number("cashout_type"));
    }

    [Fact]
    public void A_denial_is_acknowledged_and_paid_by_hand()
    {
        Start(SasCashoutValidation.System);
        CashOut();
        Ask(PendingLayout, []);

        Assert.Equal(0x00, Ask(NumberLayout, [0x00, 0, 0, 0, 0, 0, 0, 0, 1]).Number("status"));
        Assert.True(Eventually(() => Machine.HandpayPending), "the denied cash-out was never paid by hand");
        Assert.False(Directory.Exists(_wallet) && Directory.GetFiles(_wallet).Length > 0);
    }

    [Fact]
    public void The_validation_number_is_answered_before_the_ticket_prints()
    {
        // lp58 sends its reply and only then notifies the platform, which prints (lp.cpp:3629). A
        // machine that printed first answered in about 150 ms, late for a host waiting 200 ms under
        // load. The line records how many tickets the wallet held as the reply was written.
        ReplyWatch? watch = null;
        Start(SasCashoutValidation.System, inner => watch = new ReplyWatch(inner, 0x58, WalletFiles));

        Assert.Null(CashOut());
        Host.Synchronise();
        Assert.Equal(SystemValidationRequest, Poll());
        Host.Synchronise();
        Ask(PendingLayout, []);

        Assert.Equal(0x00, Ask(NumberLayout, [0x07, 0, 0, 0, 0, 0x12, 0x34, 0x56, 0x78]).Number("status"));

        Assert.Equal(0, watch!.SeenAtReply);
        Assert.True(Eventually(() => WalletFiles() == 1), "the granted ticket was never printed");
    }

    /// <summary>Notes what <paramref name="probe"/> reads at the moment the machine writes its reply to <paramref name="command"/>.</summary>
    private sealed class ReplyWatch(ISasTransport inner, byte command, Func<int> probe) : ISasTransport
    {
        public int? SeenAtReply { get; private set; }

        public string Description => inner.Description;

        public TransportCapabilities Capabilities => inner.Capabilities;

        public bool IsConnected => inner.IsConnected;

        public void Open() => inner.Open();

        public void Close() => inner.Close();

        public void Write(ReadOnlySpan<SasSymbol> symbols)
        {
            if (symbols.Length > 2 && symbols[1].Data == command && SeenAtReply is null)
                SeenAtReply = probe();

            inner.Write(symbols);
        }

        public int Read(Span<SasSymbol> destination, TimeSpan timeout) => inner.Read(destination, timeout);

        public void Dispose() => inner.Dispose();
    }
}
