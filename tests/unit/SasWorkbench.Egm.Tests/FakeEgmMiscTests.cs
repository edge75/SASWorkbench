using System.Text;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Tickets;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The software machine's remaining long polls: the note acceptor switch (lpXX_EnableDisable, QSP
/// SasNetwork_Callbacks.hpp:486), the ROM signature (lp21_RomSignature and the pending-signature branch of the
/// library's poll answer, libsas_threads.cpp:3369), the ticket text (lp7C_SetExtendedTicketData, lp7D_SetTicketData,
/// resolved as QSP's TicketDataProvenance.h:116 does), custom transfer ticket data (lp76_SetCustomAftTicketData), the
/// cash-out limit the platform never sets (lpA4_SendCashoutLimit) and the two menus. The host runs in a sequence so
/// nothing reaches the line the test did not send.
/// </summary>
public sealed class FakeEgmMiscTests : IDisposable
{
    private readonly LoopbackBus _bus = new();
    private readonly string _wallet = Path.Combine(Path.GetTempPath(), "sasworkbench-misc-" + Guid.NewGuid().ToString("N"));
    private FakeEgm? _machine;
    private PollEngine? _host;

    private FakeEgm Machine => _machine!;

    private PollEngine Host => _host!;

    private void Start(bool aft = true)
    {
        _machine = new FakeEgm(_bus.Egm, new FakeEgmOptions { Aft = aft });
        _machine.Start();
        _host = new PollEngine(_bus.Host, new PollEngineOptions
        {
            Address = SasAddress.Of(1),
            PollInterval = TimeSpan.FromMilliseconds(20),
            ResponseTimeout = TimeSpan.FromMilliseconds(300),
            QuietGap = TimeSpan.FromMilliseconds(15),
            LinkDownAfter = TimeSpan.FromSeconds(30),
        });
        _host.Start();
        Assert.True(_host.BeginSequence(), "the round did not hand the line over");
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);
    }

    public void Dispose()
    {
        _host?.Dispose();
        _machine?.Dispose();
        _bus.Dispose();
        if (Directory.Exists(_wallet))
            Directory.Delete(_wallet, recursive: true);
    }

    private static byte[] Element(byte code, string text) => [code, (byte)text.Length, .. Encoding.ASCII.GetBytes(text)];

    private static byte[] WithLength(params byte[][] parts)
    {
        byte[] body = parts.SelectMany(p => p).ToArray();
        return [(byte)body.Length, .. body];
    }

    private Ticket CashOutTwentyDollars()
    {
        Assert.True(Machine.InsertBill(2000));
        Ticket? ticket = Machine.Cashout(_wallet);
        Assert.NotNull(ticket);
        return ticket!;
    }

    [Fact]
    public void Long_poll_07_switches_the_acceptor_off_and_06_back_on()
    {
        Start();

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x07).Outcome);
        Assert.True(Machine.AcceptorDisabledByHost);
        long before = Machine.Meters.Read(SasMeterCode.TotalCreditsFromBillsAccepted);
        Assert.False(Machine.InsertBill(2000));
        Assert.Equal(before, Machine.Meters.Read(SasMeterCode.TotalCreditsFromBillsAccepted));

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x06).Outcome);
        Assert.True(Machine.InsertBill(2000));
        Assert.True(Machine.Meters.Read(SasMeterCode.TotalCreditsFromBillsAccepted) > before);
    }

    [Fact]
    public void The_action_byte_of_long_poll_08_is_ignored_as_the_platform_ignores_it()
    {
        Start();

        // Action zero asks the acceptor to stop after each note; QSP's handler leaves the flag unused (SasNetwork_Callbacks.hpp:508).
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x08, [0xFF, 0xFF, 0xFF, 0xFF, 0x00]).Outcome);
        Assert.True(Machine.InsertBill(2000));
        Assert.True(Machine.InsertBill(2000));
    }

    [Fact]
    public void A_rom_signature_is_acknowledged_and_answers_the_next_general_poll_until_acknowledged()
    {
        Start();

        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x21, [0x12, 0x34]).Outcome);

        GeneralPollResult first = Host.PollGenerally();
        Assert.Equal(GeneralPollOutcome.RomSignature, first.Outcome);
        ushort expected = FakeEgm.RomSignatureFor(0x1234);
        Assert.Equal([(byte)(expected >> 8), (byte)expected], first.Data!);

        // A repeated general poll is an implied NACK, so the signature goes again.
        Assert.Equal(GeneralPollOutcome.RomSignature, Host.PollGenerally().Outcome);

        // Any other message acknowledges it.
        Host.Synchronise();
        Assert.Equal(GeneralPollOutcome.NoActivity, Host.PollGenerally().Outcome);
    }

    [Fact]
    public void Different_seeds_give_different_signatures_and_one_seed_always_the_same()
    {
        Assert.Equal(FakeEgm.RomSignatureFor(0), FakeEgm.RomSignatureFor(0));
        Assert.NotEqual(FakeEgm.RomSignatureFor(0), FakeEgm.RomSignatureFor(1));
    }

    [Fact]
    public void With_reporting_on_the_signature_takes_a_long_poll_only_while_an_exception_is_queued()
    {
        Start();
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x0E, [0x01]).Outcome);
        Assert.Equal(LongPollOutcome.Acknowledged, Host.Execute(0x21, [0x00, 0x00]).Outcome);

        // Nothing queued: the library dispatches the long poll and keeps the signature for a general poll.
        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x1F).Outcome);

        Assert.Null(Machine.SetMenu(MachineMenu.Attendant, enter: true));
        LongPollResult taken = Host.Execute(0x1F);
        Assert.Equal(LongPollOutcome.AnsweredWithRomSignature, taken.Outcome);
    }

    [Fact]
    public void Long_poll_7C_sets_the_ticket_text_and_answers_its_status_flag_once()
    {
        Start();

        byte[] request = WithLength(Element(0x00, "CASINO ROYALE"), Element(0x01, "1 MAIN STREET"));
        LongPollResult set = Host.Execute(0x7C, request);
        Assert.Equal(LongPollOutcome.Answered, set.Outcome);
        Assert.Equal(0x01, set.Frame[2]);

        // Asked again with no lines, the flag has been cleared by the reply that carried it.
        Assert.Equal(0x00, Host.Execute(0x7C, [0x00]).Frame[2]);

        Ticket ticket = CashOutTwentyDollars();
        Assert.Equal("CASINO ROYALE", ticket.Location);
        Assert.Equal("1 MAIN STREET", ticket.Address1);
        Assert.Equal(string.Empty, ticket.Address2);
    }

    [Fact]
    public void A_broadcast_7C_is_unanswered_and_still_sets_the_text()
    {
        Start();

        LongPollResult broadcast = Host.Execute(0x7C, WithLength(Element(0x00, "EVERYWHERE")), to: SasAddress.Sync);
        Assert.Equal(LongPollOutcome.Silent, broadcast.Outcome);
        Assert.Equal(0x01, Host.Execute(0x7C, [0x00]).Frame[2]);
        Assert.Equal("EVERYWHERE", CashOutTwentyDollars().Location);
    }

    [Fact]
    public void Text_from_7C_takes_precedence_over_7D_and_7D_fills_what_7C_left()
    {
        Start();

        // Host identifier 0001, expiration 30 days, then the three lines in order.
        byte[] lines = [.. Element(0x00, "OLD LOCATION")[1..], .. Element(0x00, "OLD STREET")[1..], .. Element(0x00, "OLD TOWN")[1..]];
        LongPollResult older = Host.Execute(0x7D, WithLength([0x01, 0x00, 0x1E], lines));
        Assert.Equal(0x01, older.Frame[2]);

        Assert.Equal(LongPollOutcome.Answered, Host.Execute(0x7C, WithLength(Element(0x00, "NEW LOCATION"))).Outcome);

        Ticket ticket = CashOutTwentyDollars();
        Assert.Equal("NEW LOCATION", ticket.Location);
        Assert.Equal("OLD STREET", ticket.Address1);
        Assert.Equal("OLD TOWN", ticket.Address2);
    }

    [Fact]
    public void A_7C_with_an_unknown_data_code_is_unanswered_and_changes_nothing()
    {
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x7C, WithLength(Element(0x05, "X"))).Outcome);
        Assert.Equal(string.Empty, CashOutTwentyDollars().Location);
    }

    [Fact]
    public void A_7D_of_length_one_is_unanswered()
    {
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x7D, [0x01, 0x00]).Outcome);
    }

    [Fact]
    public void Long_poll_76_lists_the_elements_it_holds_and_a_clear_empties_them()
    {
        Start(aft: true);

        byte[] load = [.. WithLength([0x00], Element(0x00, "LOC"), Element(0x01, "ADDR"), Element(0x02, ""), Element(0x10, "TITLE"))];
        LongPollResult loaded = Host.Execute(0x76, load);
        Assert.Equal(LongPollOutcome.Answered, loaded.Outcome);
        Assert.Equal([0x03, 0x00, 0x01, 0x10], loaded.Frame[2..6]);

        LongPollResult cleared = Host.Execute(0x76, [0x01, 0x80]);
        Assert.Equal(0x00, cleared.Frame[2]);
    }

    [Fact]
    public void Long_poll_76_is_silent_on_a_machine_without_transfers()
    {
        Start(aft: false);

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0x76, [0x01, 0x80]).Outcome);
    }

    [Fact]
    public void A_76_line_as_long_as_its_buffer_is_refused()
    {
        Start(aft: true);

        string tooLong = new('A', 41);
        Assert.Equal(LongPollOutcome.Rejected, Host.Execute(0x76, WithLength([0x00], Element(0x00, tooLong))).Outcome);
    }

    [Fact]
    public void The_cash_out_limit_poll_is_unanswered_as_the_platform_sets_no_limit()
    {
        Start();

        Assert.Equal(LongPollOutcome.Silent, Host.Execute(0xA4, [0x00, 0x00]).Outcome);
    }

    [Fact]
    public void Each_menu_reports_its_entry_and_its_exit_once()
    {
        Start();

        Assert.Null(Machine.SetMenu(MachineMenu.Attendant, enter: true));
        Assert.NotNull(Machine.SetMenu(MachineMenu.Attendant, enter: true));
        Assert.Equal(0x82, Host.PollGenerally().Code);
        Host.Synchronise();
        Assert.Null(Machine.SetMenu(MachineMenu.Attendant, enter: false));
        Assert.Equal(0x83, Host.PollGenerally().Code);
        Host.Synchronise();

        Assert.Null(Machine.SetMenu(MachineMenu.Operator, enter: true));
        Assert.Equal(0x84, Host.PollGenerally().Code);
        Host.Synchronise();
        Assert.Null(Machine.SetMenu(MachineMenu.Operator, enter: false));
        Assert.Equal(0x85, Host.PollGenerally().Code);
    }
}
