using System.Text;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The chapter 16 group of 2026-11-21: a credit limit, restricted credit with its pool and
/// expiration, and turning transfers off at the machine. The protocol half is the machine library's
/// (the transfer limit check and its partial trim, the pool check, the expiration the completion
/// keeps); the rest is the platform's, in its simplest form - the transfer limit it publishes is the
/// room left under the credit limit, a win past that limit is a hand pay, restricted credit is wagered
/// first and never leaves on a cash-out ticket.
/// </summary>
public sealed partial class FakeEgmAftTests
{
    private const byte RestrictedPoolRefused = 0x8F;

    /// <summary>A transfer carrying a restricted amount, its expiration (MMDDYYYY or a count of days) and its pool.</summary>
    private static byte[] RestrictedTransfer(
        byte type, long cashable, long restricted, long nonrestricted, string id,
        long expiration = 0, ushort pool = 0, byte code = 0x00, byte flags = 0x00)
    {
        var data = new List<byte> { 0x00, code, 0x00, type };
        data.AddRange(Bcd(cashable, 5));
        data.AddRange(Bcd(restricted, 5));
        data.AddRange(Bcd(nonrestricted, 5));
        data.Add(flags);
        data.AddRange(BitConverter.GetBytes(Asset));
        data.AddRange(new byte[20]);
        data.Add((byte)id.Length);
        data.AddRange(Encoding.ASCII.GetBytes(id));
        data.AddRange(Bcd(expiration, 4));
        data.AddRange([(byte)pool, (byte)(pool >> 8)]);
        data.Add(0x00);
        data.AddRange(new byte[2]);
        data[0] = (byte)(data.Count - 1);
        return [.. data];
    }

    private static FakeEgmOptions OneCent => Quick with { AccountingDenomination = 0x01 };

    private SasDecoded TransferAndRead(byte[] request)
    {
        long status = Send72(request).Number("transfer_status")!.Value;
        return status == 0x40 ? WaitForFinal() : Interrogate();
    }

    [Fact]
    public void The_transfer_limit_is_the_room_left_under_the_credit_limit()
    {
        Start(OneCent with { CreditLimitCents = 1000 });
        Assert.Equal(1000, Lock("FF000000").Number("gaming_machine_transfer_limit"));

        Machine.InsertBill(500);
        Assert.Equal(500, Lock("FF000000").Number("gaming_machine_transfer_limit"));

        Machine.InsertBill(500);
        Assert.Equal(0, Lock("FF000000").Number("gaming_machine_transfer_limit"));
    }

    [Fact]
    public void Without_a_credit_limit_the_transfer_limit_is_the_configured_maximum()
    {
        Start(OneCent with { AftTransferLimitCents = 50_000 });
        Machine.InsertBill(2000);

        Assert.Equal(50_000, Lock("FF000000").Number("gaming_machine_transfer_limit"));
    }

    [Fact]
    public void A_credit_limit_set_at_the_machine_takes_effect_at_once()
    {
        Start(OneCent);
        Machine.InsertBill(500);

        Machine.SetCreditLimit(800);

        Assert.Equal(800, Machine.CreditLimitCents);
        Assert.Equal(300, Lock("FF000000").Number("gaming_machine_transfer_limit"));
    }

    /// <summary>The library refuses a full transfer over the limit it was given, and trims a partial one to it (lp.cpp:5382, :5547).</summary>
    [Fact]
    public void Near_the_credit_limit_a_full_transfer_is_refused_and_a_partial_one_fills_the_gap()
    {
        Start(OneCent with { CreditLimitCents = 1000 });
        Machine.InsertBill(500);
        Machine.InsertBill(100);

        SasDecoded refused = Send72(Transfer(0x00, 500, "1"));
        Assert.Equal(0x84, refused.Number("transfer_status"));

        SasDecoded partial = TransferAndRead(Transfer(0x00, 500, "2", code: 0x01));
        Assert.Equal(0x01, partial.Number("transfer_status"));
        Assert.Equal(400, partial.Number("cashable_amount"));
        Assert.Equal(1000, Machine.Meters.Read(SasMeterCode.CurrentCredits));
    }

    /// <summary>A partial transfer near the limit fills restricted first, then non-restricted, then cashable (lp.cpp:5547).</summary>
    [Fact]
    public void A_partial_transfer_near_the_limit_fills_restricted_then_nonrestricted_then_cashable()
    {
        Start(OneCent with { CreditLimitCents = 1000 });
        Machine.InsertBill(100);
        Machine.InsertBill(100);
        Machine.InsertBill(200);

        SasDecoded final = TransferAndRead(RestrictedTransfer(0x00, 100, 500, 400, "1", pool: 1, code: 0x01));

        Assert.Equal(0x01, final.Number("transfer_status"));
        Assert.Equal(500, final.Number("restricted_amount"));
        Assert.Equal(100, final.Number("nonrestricted_amount"));
        Assert.Equal(0, final.Number("cashable_amount"));
    }

    /// <summary>The platform hands a win that would take the credit meter past its limit to an attendant (QSP CombinedAwardIsHandpaid).</summary>
    [Fact]
    public void A_win_past_the_credit_limit_is_a_hand_pay_and_not_credited()
    {
        Start(OneCent with { CreditLimitCents = 1000, CentsPerGameCredit = 1 });
        Machine.InsertBill(500);
        Drain();

        Machine.PlayGames(1, 100, winCredits: 700);

        Assert.Equal(400, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Contains(HandpayPending, Drain());
        Assert.True(Machine.ResetHandpay());
        Assert.Equal(700, Machine.Meters.Read(SasMeterCode.TotalJackpotCredits));
        Assert.Equal(400, Machine.Meters.Read(SasMeterCode.CurrentCredits));
    }

    /// <summary>
    /// This machine holds one hand pay at a time, so a second win past the limit while the first is
    /// still held must be refused before the round moves any meter; otherwise the wager is taken and
    /// the win is neither credited nor owed.
    /// </summary>
    [Fact]
    public void A_second_win_past_the_credit_limit_while_a_hand_pay_is_held_is_refused_before_any_meter_moves()
    {
        Start(OneCent with { CreditLimitCents = 1000, CentsPerGameCredit = 1 });
        Machine.InsertBill(500);
        Machine.PlayGames(1, 100, winCredits: 700);
        long played = Machine.Meters.Read(SasMeterCode.GamesPlayed);

        Assert.Throws<InvalidOperationException>(() => Machine.PlayGames(1, 100, winCredits: 900));

        Assert.Equal(400, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(played, Machine.Meters.Read(SasMeterCode.GamesPlayed));
    }

    [Fact]
    public void A_win_within_the_credit_limit_is_credited_as_before()
    {
        Start(OneCent with { CreditLimitCents = 1000, CentsPerGameCredit = 1 });
        Machine.InsertBill(500);

        Machine.PlayGames(1, 100, winCredits: 600);

        Assert.Equal(1000, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.TotalJackpotCredits));
    }

    /// <summary>The completion keeps the pool and the expiration against the restricted balance (libsas_internal.cpp:3275).</summary>
    [Fact]
    public void Restricted_credit_lands_with_its_pool_and_expiration_and_74_reports_them()
    {
        Start(OneCent);

        SasDecoded final = TransferAndRead(RestrictedTransfer(0x00, 0, 500, 0, "1", expiration: 12312030, pool: 1));

        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(500, final.Number("restricted_amount"));
        Assert.Equal(1, final.Number("pool_id"));
        Assert.Equal(12312030, final.Number("expiration"));

        SasDecoded status = Lock("FF000000");
        Assert.Equal(500, status.Number("current_restricted_amount"));
        Assert.Equal(0, status.Number("current_cashable_amount"));
        Assert.Equal(1, status.Number("restricted_pool_id"));
        Assert.Equal(12312030, status.Number("restricted_expiration"));

        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.CurrentRestrictedCredits));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.InHouseRestrictedTransfersToGamingMachine_Cents));
        Assert.Equal(1, Machine.Meters.Read(SasMeterCode.InHouseTransfersToGamingMachineThatIncludedRestrictedAmounts_Qty));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.ElectronicRestrictedPromotionalTransfersToGamingMachine_Crd));
    }

    /// <summary>The machine holds one pool at a time and refuses another while any of the first remains (lp.cpp:5459).</summary>
    [Fact]
    public void Restricted_credit_from_another_pool_is_refused_while_the_first_remains()
    {
        Start(OneCent);
        TransferAndRead(RestrictedTransfer(0x00, 0, 500, 0, "1", pool: 1));

        Assert.Equal(0x00, TransferAndRead(RestrictedTransfer(0x00, 0, 500, 0, "2", pool: 1)).Number("transfer_status"));
        SasDecoded refused = Send72(RestrictedTransfer(0x00, 0, 500, 0, "3", pool: 2));

        Assert.Equal(RestrictedPoolRefused, refused.Number("transfer_status"));
        Assert.Equal(1000, Lock("FF000000").Number("current_restricted_amount"));
    }

    /// <summary>A later date wins over an earlier one, and a count of days over a date (calculateExpirationDate, libsas_utils.cpp:614).</summary>
    [Fact]
    public void Two_expirations_combine_the_way_the_library_combines_them()
    {
        Start(OneCent);
        TransferAndRead(RestrictedTransfer(0x00, 0, 100, 0, "1", expiration: 06302030, pool: 1));
        TransferAndRead(RestrictedTransfer(0x00, 0, 100, 0, "2", expiration: 01152031, pool: 1));
        Assert.Equal(01152031, Lock("FF000000").Number("restricted_expiration"));

        TransferAndRead(RestrictedTransfer(0x00, 0, 100, 0, "3", expiration: 30, pool: 1));
        Assert.Equal(30, Lock("FF000000").Number("restricted_expiration"));
    }

    /// <summary>Emptying the restricted balance gives its pool and expiration back to the default (libsas_internal.cpp:5546).</summary>
    [Fact]
    public void A_transfer_to_the_host_takes_restricted_credit_and_an_empty_balance_has_no_pool()
    {
        Start(OneCent);
        TransferAndRead(RestrictedTransfer(0x00, 200, 500, 0, "1", expiration: 12312030, pool: 1));

        SasDecoded partial = TransferAndRead(RestrictedTransfer(0x80, 0, 300, 0, "2"));
        Assert.Equal(0x00, partial.Number("transfer_status"));
        Assert.Equal(300, partial.Number("restricted_amount"));
        Assert.Equal(200, Lock("FF000000").Number("current_restricted_amount"));
        Assert.Equal(300, Machine.Meters.Read(SasMeterCode.InHouseRestrictedTransfersToHost_Cents));
        Assert.Equal(300, Machine.Meters.Read(SasMeterCode.ElectronicRestrictedPromotionalTransfersToHost));

        TransferAndRead(RestrictedTransfer(0x80, 0, 9_999_999_999, 0, "3", code: 0x01));
        SasDecoded empty = Lock("FF000000");
        Assert.Equal(0, empty.Number("current_restricted_amount"));
        Assert.Equal(0, empty.Number("restricted_pool_id"));
        Assert.Equal(0, empty.Number("restricted_expiration"));
        Assert.Equal(200, empty.Number("current_cashable_amount"));
    }

    /// <summary>A full transfer to the host may name restricted credit up to what the machine holds (lp.cpp:5443).</summary>
    [Fact]
    public void A_full_transfer_to_the_host_of_more_restricted_credit_than_held_is_refused()
    {
        Start(OneCent);
        TransferAndRead(RestrictedTransfer(0x00, 0, 500, 0, "1", pool: 1));

        Assert.Equal(0x83, Send72(RestrictedTransfer(0x80, 0, 600, 0, "2")).Number("transfer_status"));
    }

    /// <summary>The platform wagers restricted credit first, then non-restricted, then cashable (QSP NvramHandler.cpp:947).</summary>
    [Fact]
    public void Restricted_credit_is_played_first_then_nonrestricted_then_cashable()
    {
        Start(OneCent with { CentsPerGameCredit = 1 });
        TransferAndRead(RestrictedTransfer(0x00, 100, 100, 100, "1", pool: 1));

        Machine.PlayGames(1, 150);
        Assert.Equal(100, Machine.Meters.Read(SasMeterCode.TotalRestrictedAmountPlayed_Crd));
        Assert.Equal(50, Machine.Meters.Read(SasMeterCode.TotalNonrestrictedAmountPlayed_Crd));
        Assert.Equal(0, Machine.Meters.Read(SasMeterCode.CurrentRestrictedCredits));

        SasDecoded status = Lock("FF000000");
        Assert.Equal(0, status.Number("current_restricted_amount"));
        Assert.Equal(50, status.Number("current_nonrestricted_amount"));
        Assert.Equal(100, status.Number("current_cashable_amount"));
    }

    /// <summary>Restricted credit may not leave on a cash-out ticket, so a cash-out pays the rest and leaves it (QSP QSP_COLLECT_NOT_PAID_RESTRICTED_CREDIT).</summary>
    [Fact]
    public void A_cashout_pays_cashable_and_nonrestricted_credit_and_leaves_restricted_on_the_machine()
    {
        Start(OneCent);
        TransferAndRead(RestrictedTransfer(0x00, 300, 500, 200, "1", pool: 1));

        Ticket? ticket = Machine.Cashout(_wallet);

        Assert.NotNull(ticket);
        Assert.Equal(500, ticket!.AmountCents);
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Lock("FF000000").Number("current_restricted_amount"));
        Assert.Null(Machine.Cashout(_wallet));
    }

    /// <summary>Flag bit 3 on a zero transfer to the host has the machine cash out its usual way, restricted credit staying (QSP ClassifyAftHostCashoutRequest).</summary>
    [Fact]
    public void A_zero_transfer_to_the_host_asking_for_a_cashout_pays_the_rest_and_leaves_restricted_credit()
    {
        Start(OneCent with { WalletDirectory = _wallet });
        TransferAndRead(RestrictedTransfer(0x00, 300, 500, 200, "1", pool: 1));

        SasDecoded final = TransferAndRead(RestrictedTransfer(0x80, 0, 0, 0, "2", flags: 0x08));

        Assert.Equal(0x00, final.Number("transfer_status"));
        Assert.Equal(0, final.Number("cashable_amount"));
        Assert.Single(Directory.GetFiles(_wallet));
        Assert.Equal(500, Machine.Meters.Read(SasMeterCode.CurrentCredits));
        Assert.Equal(500, Lock("FF000000").Number("current_restricted_amount"));
    }

    /// <summary>With amounts named, the transfer happens first and only the remainder is cashed out.</summary>
    [Fact]
    public void A_transfer_to_the_host_asking_for_a_cashout_takes_what_it_named_and_pays_the_remainder()
    {
        Start(OneCent with { WalletDirectory = _wallet });
        TransferAndRead(RestrictedTransfer(0x00, 500, 500, 500, "1", pool: 1));

        SasDecoded final = TransferAndRead(RestrictedTransfer(0x80, 0, 600, 0, "2", code: 0x01, flags: 0x08));

        Assert.Equal(0x01, final.Number("transfer_status"));
        Assert.Equal(500, final.Number("restricted_amount"));
        Assert.Equal(0, final.Number("cashable_amount"));
        WaitFor(() => Machine.Meters.Read(SasMeterCode.CurrentCredits) == 0);
        Assert.Single(Directory.GetFiles(_wallet));
    }

    private static void WaitFor(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the machine never got there");
            Thread.Sleep(20);
        }
    }

    [Fact]
    public void Turning_transfers_off_clears_the_feature_bit_and_silences_74()
    {
        Start(OneCent);
        Assert.Equal(0x00, RegistrationStatus(Registration(0x00, KeyA, 1)));

        Machine.SetAft(false);

        Assert.False(Machine.AftOn);
        LongPollResult silent = Host.Execute(0x74, Convert.FromHexString("FF000000"));
        Assert.NotEqual(LongPollOutcome.Answered, silent.Outcome);
        Assert.Contains(RegistrationCancelled, Drain());

        Machine.SetAft(true);
        Assert.Equal(0xFF, LockStatus("FF000000"));
    }
}
