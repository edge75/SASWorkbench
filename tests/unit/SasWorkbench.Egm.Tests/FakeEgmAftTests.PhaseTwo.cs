using System.Text;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// Phase two of the software machine's funds transfer, against the machine library's
/// lp73_RegisterGamingMachine, the registration and receipt checks of lp72_TransferFunds, and the
/// platform's bonus and receipt handling: registration and the three exceptions it raises, debit
/// transfers as far as registration takes them, bonus awards of both kinds, non-restricted credit,
/// and the receipt status a transfer reports.
/// </summary>
public sealed partial class FakeEgmAftTests
{
    private const byte RequestToRegister = 0x6C;
    private const byte RegistrationAcknowledged = 0x6D;
    private const byte RegistrationCancelled = 0x6E;
    private const byte HandpayPending = 0x51;

    private static readonly SasMessageLayout RegistrationLayout = SasLayouts.For(0x73)!;

    private static readonly byte[] KeyA = Key("1234567890");
    private static readonly byte[] KeyB = Key("0987654321");

    private SasDecoded Send73(byte[] data)
    {
        LongPollResult result = Host.Execute(0x73, data);
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        SasDecoded decoded = SasMessageDecoder.DecodeResponse(RegistrationLayout, result.Frame);
        Assert.True(decoded.Succeeded, decoded.Failure);
        return decoded;
    }

    /// <summary>A $73 that carries an asset number, key and point of sale: initialise, register or ask for the attendant.</summary>
    private static byte[] Registration(byte code, byte[] key, uint pos, uint asset = Asset) =>
    [
        0x1D, code,
        (byte)asset, (byte)(asset >> 8), (byte)(asset >> 16), (byte)(asset >> 24),
        .. key,
        (byte)pos, (byte)(pos >> 8), (byte)(pos >> 16), (byte)(pos >> 24),
    ];

    private static byte[] Key(string digits)
    {
        byte[] key = new byte[20];
        Encoding.ASCII.GetBytes(digits).CopyTo(key, 0);
        return key;
    }

    private long RegistrationStatus(byte[] data) => Send73(data).Number("registration_status")!.Value;

    /// <summary>A transfer request with every field a phase two case needs, at the library's fixed offsets.</summary>
    private static byte[] FullTransfer(
        byte type, long cashable, long nonrestricted, string id, byte flags = 0x00, byte[]? key = null,
        byte[]? receipt = null, long restricted = 0)
    {
        var data = new List<byte> { 0x00, 0x00, 0x00, type };
        data.AddRange(Bcd(cashable, 5));
        data.AddRange(Bcd(restricted, 5));
        data.AddRange(Bcd(nonrestricted, 5));
        data.Add(flags);
        data.AddRange(BitConverter.GetBytes(Asset));
        data.AddRange(key ?? new byte[20]);
        data.Add((byte)id.Length);
        data.AddRange(Encoding.ASCII.GetBytes(id));
        data.AddRange(new byte[4 + 2]);
        data.Add((byte)(receipt?.Length ?? 0));
        data.AddRange(receipt ?? []);
        data.AddRange(new byte[2]);
        data[0] = (byte)(data.Count - 1);
        return [.. data];
    }

    /// <summary>Receipt data carrying a patron account, the one field an in-house receipt cannot go without.</summary>
    private static byte[] PatronAccount(string account) =>
        [0x11, (byte)account.Length, .. Encoding.ASCII.GetBytes(account)];

    private void Register(uint pos = 0x00012345)
    {
        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, pos)));
        Assert.Equal(0x01, RegistrationStatus(Registration(0x01, KeyA, pos)));
    }

    [Fact]
    public void Initialising_stores_no_key_and_registering_stores_both()
    {
        Start();

        SasDecoded ready = Send73(Registration(0x00, KeyA, 0x00012345));
        Assert.Equal(0x00, ready.Number("registration_status"));
        Assert.Equal(Asset, ready.Number("asset_number"));
        Assert.Equal(new byte[20], ready["registration_key"]!.Bytes);
        Assert.Equal(0, ready.Number("pos_id"));

        SasDecoded registered = Send73(Registration(0x01, KeyB, 0x00054321));
        Assert.Equal(0x01, registered.Number("registration_status"));
        Assert.Equal(KeyB, registered["registration_key"]!.Bytes);
        Assert.Equal(0x00054321, registered.Number("pos_id"));

        SasDecoded read = Send73([0x01, 0xFF]);
        Assert.Equal(0x01, read.Number("registration_status"));
        Assert.Equal(KeyB, read["registration_key"]!.Bytes);
        Assert.Empty(Drain());
    }

    [Fact]
    public void Another_asset_number_cancels_a_registration_in_progress()
    {
        Start();
        Assert.Equal(0x80, RegistrationStatus(Registration(0x00, KeyA, 1, asset: 54321)));
        Assert.Empty(Drain());

        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, 1)));
        SasDecoded refused = Send73(Registration(0x01, KeyA, 1, asset: 54321));

        Assert.Equal(0x80, refused.Number("registration_status"));
        Assert.Equal(Asset, refused.Number("asset_number"));
        Assert.Equal([RegistrationCancelled], Drain());
    }

    [Fact]
    public void Cancelling_raises_6e_only_when_it_changes_something()
    {
        Start();
        Assert.Equal(0x80, RegistrationStatus([0x01, 0x80]));
        Assert.Empty(Drain());

        Register();
        Assert.Equal(0x80, RegistrationStatus([0x01, 0x80]));
        Assert.Equal(0x80, RegistrationStatus([0x01, 0x80]));
        Assert.Equal([RegistrationCancelled], Drain());
    }

    [Fact]
    public void Registering_with_a_zero_key_is_out_of_order_and_cancels()
    {
        Start();
        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, 1)));

        Assert.Equal(0x80, RegistrationStatus(Registration(0x01, new byte[20], 1)));
        Assert.Equal([RegistrationCancelled], Drain());
    }

    [Fact]
    public void The_attendant_asks_to_register_and_the_machine_stays_unregistered()
    {
        Start();

        Assert.Null(Machine.AttendantRegister());

        Assert.Equal([RequestToRegister], Drain());
        Assert.Equal(0x80, RegistrationStatus([0x01, 0xFF]));
    }

    [Fact]
    public void The_attendant_acknowledges_a_registration_the_host_waits_on()
    {
        Start();
        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, 1)));
        Assert.Equal(0x40, RegistrationStatus(Registration(0x40, KeyA, 1)));

        Assert.Null(Machine.AttendantRegister());

        Assert.Equal([RegistrationAcknowledged], Drain());
        Assert.Equal(0x00, RegistrationStatus([0x01, 0xFF]));
        Assert.Equal(0x01, RegistrationStatus(Registration(0x01, KeyA, 1)));
        Assert.NotNull(Machine.AttendantRegister());
    }

    [Fact]
    public void A_loop_break_cancels_a_registration_that_was_not_finished()
    {
        Start(new FakeEgmOptions { LinkLossAfter = TimeSpan.FromSeconds(1.5) });
        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, 1)));

        Thread.Sleep(2000);

        Assert.Equal(0x80, Machine.AftRegistrationStatus);
        Assert.Contains(RegistrationCancelled, Drain());
    }

    [Fact]
    public void A_registered_machine_says_so_in_its_aft_status_until_its_asset_number_changes()
    {
        Start();
        Assert.Equal(0xB3, Lock("FF000000").Number("aft_status"));

        Register();
        Assert.Equal(0xBB, Lock("FF000000").Number("aft_status"));

        Machine.SetAssetNumber(54321);
        Assert.Equal(0xB3, Lock("FF000000").Number("aft_status"));
        Assert.Equal([RegistrationCancelled], Drain());
    }

    [Fact]
    public void A_debit_transfer_is_refused_by_registration_and_failed_by_the_platform()
    {
        Start(Quick);
        Assert.Equal(0x88, Send72(FullTransfer(0x40, 500, 0, "1", key: KeyA)).Number("transfer_status"));

        Register(pos: 0);
        SasDecoded noPos = Send72(FullTransfer(0x40, 500, 0, "2", key: KeyA));
        Assert.Equal(0x8A, noPos.Number("transfer_status"));
        Assert.Equal(0, noPos.Number("transaction_buffer_position"));

        Register();
        Assert.Equal(0x89, Send72(FullTransfer(0x40, 500, 0, "3", key: KeyB)).Number("transfer_status"));
        Assert.Equal(0x40, Send72(FullTransfer(0x40, 500, 0, "4", key: KeyA)).Number("transfer_status"));
        Assert.Equal(0x82, WaitForFinal().Number("transfer_status"));
    }

    [Fact]
    public void The_enabled_features_carry_the_bonus_bit_until_bonusing_is_turned_off()
    {
        Start(Quick);
        SasMessageLayout features = SasLayouts.For(0xA0)!;

        long Bit() => SasMessageDecoder.DecodeResponse(features, Host.Execute(0xA0, [0x00, 0x00]).Frame)
            .Number("feature_codes_1")!.Value & 0x02;

        Assert.Equal(0x02, Bit());
        Assert.Equal(0x13, Lock("FF000000").Number("available_transfers"));

        Machine.SetAftBonus(false);

        Assert.Equal(0x00, Bit());
        Assert.Equal(0x03, Lock("FF000000").Number("available_transfers"));
        Assert.Equal(0x87, Send72(FullTransfer(0x10, 500, 0, "1")).Number("transfer_status"));
    }

    [Fact]
    public void A_coin_out_bonus_is_paid_to_the_credit_meter_as_a_machine_paid_win()
    {
        Start(Quick);
        long credits = Machine.Meters.Read(SasMeterCode.CurrentCredits);

        Assert.Equal(0x40, Send72(FullTransfer(0x10, 500, 300, "1")).Number("transfer_status"));
        SasDecoded final = WaitForFinal();

        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(500, final.Number("cashable_amount"));
        Assert.Equal(300, final.Number("nonrestricted_amount"));
        long cents = Machine.AccountingCents;
        Assert.Equal(credits + (800 / cents), Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.BonusCashableTransfersToGamingMachine_Cents));
        Assert.Equal(1, Machine.Meters.Read(SasMeterCode.BonusTransfersToGamingMachineThatIncludedCashableAmounts_Qty));
        Assert.Equal(300, Machine.Meters.Read(SasMeterCode.BonusNonrestrictedTransfersToGamingMachine_Cents));
        Assert.Equal(800 / cents, Machine.Meters.Read(SasMeterCode.TotalMachinePaidExternalBonusWin_Crd));
        Assert.Equal(300, Lock("FF000000").Number("current_nonrestricted_amount"));
    }

    [Fact]
    public void Non_restricted_credit_is_played_first_and_counted_as_played()
    {
        Start(Quick with { AccountingDenomination = 0x01 });
        Send72(FullTransfer(0x10, 0, 300, "1"));
        WaitForFinal();

        Machine.PlayGames(1, 1);

        Assert.Equal(100, Machine.Meters.Read(SasMeterCode.TotalNonrestrictedAmountPlayed_Crd));
        Assert.Equal(200, Lock("FF000000").Number("current_nonrestricted_amount"));
    }

    [Fact]
    public void A_jackpot_bonus_of_nothing_still_locks_the_machine_up_in_a_hand_pay()
    {
        Start(Quick);

        Assert.Equal(0x40, Send72(FullTransfer(0x11, 0, 0, "1")).Number("transfer_status"));

        SasDecoded final = WaitForFinal();
        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(1, final.Number("transaction_buffer_position"));
        Assert.True(Machine.HandpayPending);
        Assert.Contains(HandpayPending, Drain());
    }

    [Fact]
    public void A_jackpot_bonus_is_paid_by_the_attendant_as_an_external_bonus()
    {
        Start(Quick with { AccountingDenomination = 0x01 });
        long credits = Machine.Meters.Read(SasMeterCode.CurrentCredits);

        Send72(FullTransfer(0x11, 500, 0, "1"));
        WaitForFinal();
        Host.Execute(0x1B, []);
        Host.Execute(0x1F, []);
        Assert.True(Machine.ResetHandpay());

        Assert.Equal(credits, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.TotalAttendantPaidExternalBonusWin_Crd));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.BonusCashableTransfersToGamingMachine_Cents));
    }

    [Fact]
    public void A_bonus_refuses_restricted_credit_and_a_receipt()
    {
        Start(Quick);

        Assert.Equal(0x82, Send72(FullTransfer(0x10, 500, 0, "1", restricted: 500)).Number("transfer_status"));
        Assert.Equal(0x92, Send72(FullTransfer(0x10, 500, 0, "2", flags: 0x80, receipt: PatronAccount("123")))
            .Number("transfer_status"));
    }

    [Fact]
    public void An_in_house_receipt_without_a_patron_account_is_refused_91()
    {
        Start(Quick);

        SasDecoded refused = Send72(FullTransfer(0x00, 500, 0, "1", flags: 0x80));

        Assert.Equal(0x91, refused.Number("transfer_status"));
        Assert.Equal(0xFF, refused.Number("receipt_status"));
    }

    [Fact]
    public void A_receipt_prints_after_the_transfer_and_reopens_a_completion_already_read()
    {
        Start(Quick with { AftReceiptPrintDelay = TimeSpan.FromMilliseconds(800) });

        SasDecoded pending = Send72(FullTransfer(0x00, 500, 0, "1", flags: 0x80, receipt: PatronAccount("123456789")));
        Assert.Equal(0x40, pending.Number("transfer_status"));
        Assert.Equal(0x40, pending.Number("receipt_status"));

        SasDecoded final = WaitForFinal();
        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(0x20, final.Number("receipt_status"));
        Assert.Equal(0, Machine.PendingExceptionsOn(Core.Wire.SasChannel.One));

        Thread.Sleep(1200);

        Assert.Contains(TransferComplete, Drain());
        SasDecoded printed = Interrogate();
        Assert.Equal(0x00, printed.Number("receipt_status"));
        Assert.Equal(1, printed.Number("transaction_buffer_position"));
        Assert.Equal(2, Machine.AftHistoryIndex);
    }

    [Fact]
    public void A_receipt_is_filed_where_the_machine_was_told_to_file_it()
    {
        string receipts = Path.Combine(_wallet, "receipts");
        Start(Quick with { AftReceiptPrintDelay = TimeSpan.Zero, ReceiptDirectory = receipts });

        Send72(FullTransfer(0x00, 500, 0, "7", flags: 0x80, receipt: PatronAccount("4242")));
        WaitForFinal();
        Thread.Sleep(200);

        string text = File.ReadAllText(Assert.Single(Directory.GetFiles(receipts)));
        Assert.Contains("account 4242", text, StringComparison.Ordinal);
        Assert.Contains("transaction 7", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second receipt that starts printing before the first is out must not take the first's
    /// place: each print is the platform's own report, so both transfers end up printed ($00) and
    /// neither is left at printing ($20) for a host to read forever.
    /// </summary>
    [Fact]
    public void Two_receipts_printing_at_once_both_settle()
    {
        Start(Quick with { AftReceiptPrintDelay = TimeSpan.FromMilliseconds(800) });

        Send72(FullTransfer(0x00, 500, 0, "1", flags: 0x80, receipt: PatronAccount("111")));
        Assert.Equal(0x20, WaitForFinal().Number("receipt_status"));
        Send72(FullTransfer(0x00, 500, 0, "2", flags: 0x80, receipt: PatronAccount("222")));
        Assert.Equal(0x20, WaitForFinal().Number("receipt_status"));

        Thread.Sleep(1500);

        Assert.Equal(0x00, Interrogate(1).Number("receipt_status"));
        Assert.Equal(0x00, Interrogate(2).Number("receipt_status"));
    }

    /// <summary>
    /// A receipt directory that cannot be written loses only the text file: the status the host
    /// reads is still printed, the machine keeps answering, and the reason is there to read.
    /// </summary>
    [Fact]
    public void A_receipt_that_cannot_be_filed_is_reported_and_the_machine_carries_on()
    {
        string blocker = Path.Combine(_wallet, "not-a-directory");
        Directory.CreateDirectory(_wallet);
        File.WriteAllText(blocker, "a file where the receipts directory should be");
        Start(Quick with { AftReceiptPrintDelay = TimeSpan.Zero, ReceiptDirectory = blocker });

        Send72(FullTransfer(0x00, 500, 0, "8", flags: 0x80, receipt: PatronAccount("4242")));
        WaitForFinal();
        Thread.Sleep(200);

        Assert.Equal(0x00, Interrogate(1).Number("receipt_status"));
        Assert.Contains("receipt 1 not filed", Machine.ReceiptFilingError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The library checks the code and the length before it looks at AFT (lp.cpp:6146, :6156,
    /// then :6173), so a malformed $73 is NACKed even by a machine with funds transfer off, while
    /// a well-formed one goes unanswered.
    /// </summary>
    [Fact]
    public void A_malformed_73_is_nacked_even_with_funds_transfer_off()
    {
        Start(new FakeEgmOptions { Aft = false });

        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x73, [0x01, 0x42]).Outcome);
        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x73, [0x01, 0x00]).Outcome);
        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x73, [0x01, 0xFF]).Outcome);
    }
}
