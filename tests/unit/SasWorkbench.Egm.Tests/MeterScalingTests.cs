using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm.Tests;

/// <summary>
/// The disagreement three waves deferred the meter index for, produced on a wire and measured.
/// </summary>
/// <remarks>
/// <para>
/// The gaming machine library reads a meter two ways. Its shared single-meter helper divides the
/// value by the accounting denomination in cents, for a meter it does not call multigame, when the
/// machine is in either of its two default meter modes; the bulk replies read the same meter and
/// write it as it stands. Every previous wave stated that from the source and stopped, because
/// nothing here could produce it: the real machine cannot, and the software machine did not model
/// it.
/// </para>
/// <para>
/// It can now, and these are the bytes. One twenty-unit note, read back through the bulk bill
/// meters and through the dedicated count for that denomination, on two machines that differ in
/// nothing but their meter mode.
/// </para>
/// <para>
/// <b>The mode that matters is the default.</b> The platform above the library sets full manual on
/// the machine this project tests against, and writes it to that machine's memory, so on that
/// machine the two commands cannot disagree at any denomination. That is why the index could be
/// widened at all — and why the software machine is in full manual too unless a case asks
/// otherwise.
/// </para>
/// </remarks>
public class MeterScalingTests
{
    private static readonly SasAddress Address = SasAddress.Of(1);

    /// <summary>A nickel machine, which is what the real one publishes.</summary>
    private static FakeEgmOptions Nickel(SasMeterMode mode) => new()
    {
        AccountingDenomination = 0x02,
        MeterMode = mode,
    };

    /// <summary>
    /// In full manual the two commands answer the same bytes, note for note.
    /// </summary>
    [Fact]
    public void In_full_manual_the_bulk_reply_and_the_dedicated_count_answer_the_same_number()
    {
        using var rig = new Rig(Nickel(SasMeterMode.FullManual));
        rig.Machine.InsertBill(2000);

        Assert.Equal("011E000000000000000000000000000000010000000000000000DFFD",
            rig.Host.Execute(0x1E).ToHex());
        Assert.Equal("01350000000127E7", rig.Host.Execute(0x35).ToHex());

        Assert.Equal(1, Field(rig, 0x1E, "bills_20"));
        Assert.Equal(1, Field(rig, 0x35, "bills_20"));
    }

    /// <summary>
    /// In the library's default mode they do not, and the dedicated count answers zero.
    /// </summary>
    /// <remarks>
    /// One note, divided by five because the machine counts in nickels, is nought — so a host
    /// reading the twenty-unit note count from long poll <c>$35</c> on such a machine is told the
    /// machine has taken none. That is the number a test would have started measuring had the
    /// index been widened by preferring the dedicated command, and it is why a count is read from
    /// the reply that does not divide it.
    /// </remarks>
    [Fact]
    public void In_the_default_mode_the_dedicated_count_divides_a_number_of_notes_by_a_currency_unit()
    {
        using var rig = new Rig(Nickel(SasMeterMode.DefaultAuto));
        rig.Machine.InsertBill(2000);

        // The bulk reply is the same bytes as in full manual; the dedicated count is not.
        Assert.Equal("011E000000000000000000000000000000010000000000000000DFFD",
            rig.Host.Execute(0x1E).ToHex());
        Assert.Equal("013500000000AEF6", rig.Host.Execute(0x35).ToHex());

        Assert.Equal(1, Field(rig, 0x1E, "bills_20"));
        Assert.Equal(0, Field(rig, 0x35, "bills_20"));
    }

    /// <summary>
    /// A multigame meter is left alone in every mode, which is why <c>$11</c> and <c>$19</c> agree.
    /// </summary>
    /// <remarks>
    /// The library's own predicate names fourteen meters the helper does not touch, and credits
    /// wagered is one of them. So the three names read from a single-meter command that reports a
    /// multigame meter are safe in either mode, and this is the assertion that says so.
    /// </remarks>
    [Fact]
    public void A_multigame_meter_is_the_same_number_in_either_mode()
    {
        foreach (SasMeterMode mode in (SasMeterMode[])[SasMeterMode.FullManual, SasMeterMode.DefaultAuto])
        {
            using var rig = new Rig(Nickel(mode));
            rig.Machine.InsertBill(10000);
            rig.Machine.PlayGames(2, betCredits: 1, winCredits: 0);

            Assert.Equal(40, Field(rig, 0x11, "total_coin_in"));
            Assert.Equal(40, Field(rig, 0x19, "total_coin_in"));
            Assert.Equal(2, Field(rig, 0x15, "games_played"));
            Assert.Equal(2, Field(rig, 0x1C, "games_played"));
        }
    }

    /// <summary>
    /// A credit meter that is not multigame is where the division is the library's own correction.
    /// </summary>
    /// <remarks>
    /// Credits dropped in is counted in the same unit as everything else here, so this machine's
    /// two answers differ by the denomination — four hundred units through the bulk reply and
    /// eighty through the single-meter poll. Which of those a real machine's platform means
    /// depends on the unit it wrote the meter in, and no reply says; what is not in doubt is that
    /// they are two numbers, which is the whole point of choosing between them deliberately.
    /// </remarks>
    [Fact]
    public void A_credit_meter_that_is_not_multigame_differs_between_the_two_commands()
    {
        using var rig = new Rig(Nickel(SasMeterMode.DefaultAuto));
        rig.Machine.InsertBill(2000);

        Assert.Equal(400, Field(rig, 0x0F, "total_drop"));
        Assert.Equal(80, Field(rig, 0x13, "total_drop"));
    }

    /// <summary>
    /// The two counts long poll <c>$18</c> answers are meters, and the selected-meter poll agrees.
    /// </summary>
    /// <remarks>
    /// They were kept as counters of this machine's own until now, under a note saying the
    /// protocol keeps no code for either. It keeps two, and the library's handler for <c>$18</c>
    /// reads them by code — so asking for the same code through <c>$2F</c> must give the same
    /// number, and on a machine where it did not, one of the two would be a fiction.
    /// </remarks>
    [Fact]
    public void The_games_since_counts_answer_the_same_through_either_command()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(10000);
        rig.Machine.PlayGames(3, betCredits: 1, winCredits: 0);

        Assert.Equal(3, Field(rig, 0x18, "games_since_power_up"));

        SasDecoded selected = Decode(rig, 0x2F, "03000025");
        Assert.Equal(0x25, selected.Number("meters[0].code"));
        Assert.Equal(3, selected.Number("meters[0].value"));
    }

    /// <summary>Shutting the slot door starts the second count again and leaves the first.</summary>
    [Fact]
    public void Shutting_the_slot_door_restarts_only_the_count_that_is_kept_from_it()
    {
        using var rig = new Rig();
        rig.Machine.InsertBill(10000);
        rig.Machine.PlayGames(2, betCredits: 1, winCredits: 0);
        rig.Machine.SetDoor("slot", true);
        rig.Machine.SetDoor("slot", false);
        rig.Machine.PlayGames(1, betCredits: 1, winCredits: 0);

        Assert.Equal(3, Field(rig, 0x18, "games_since_power_up"));
        Assert.Equal(1, Field(rig, 0x18, "games_since_door_closure"));
    }

    private static SasDecoded Decode(Rig rig, byte command, string request = "")
    {
        LongPollResult result = rig.Host.Execute(command, Convert.FromHexString(request));
        Assert.Equal(LongPollOutcome.Answered, result.Outcome);
        return SasMessageDecoder.DecodeResponse(SasLayouts.For(command)!, result.Frame);
    }

    private static long Field(Rig rig, byte command, string field)
    {
        SasDecoded decoded = Decode(rig, command);
        Assert.True(decoded.Succeeded, decoded.Failure);
        long? value = decoded.Number(field);
        Assert.NotNull(value);
        return value.Value;
    }

    private sealed class Rig : IDisposable
    {
        private readonly LoopbackBus _bus = new();

        public Rig(FakeEgmOptions? options = null)
        {
            Machine = new FakeEgm(_bus.Egm, options);
            Machine.Start();

            Host = new PollEngine(_bus.Host, new PollEngineOptions
            {
                Address = Address,
                PollInterval = TimeSpan.FromMilliseconds(20),
                ResponseTimeout = TimeSpan.FromSeconds(5),
                QuietGap = TimeSpan.FromMilliseconds(15),
                LinkDownAfter = TimeSpan.FromSeconds(30),
            });
            Host.Start();
        }

        public FakeEgm Machine { get; }

        public PollEngine Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            Machine.Dispose();
            _bus.Dispose();
        }
    }
}
