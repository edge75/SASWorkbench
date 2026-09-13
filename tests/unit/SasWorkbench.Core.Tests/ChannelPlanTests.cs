using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// How a machine is divided between hosts, and the one arrangement that has to be refused.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here states a fact about the gaming machine library that was copied by hand. The seven
/// categories are generated from its channel-control structure; which category owns a command is
/// generated from the gate at the top of that command's handler; which owns an exception is
/// generated from the five predicates that decide it; and the enabled-feature bits a channel
/// without a category must not report are generated from the function that clears them. What is
/// asserted below is that the tables are self-consistent and that the rules built on them behave.
/// </para>
/// <para>
/// The three tables were also checked against the revision of the library the machine under test
/// actually runs rather than only against the checkout the generator reads — twenty-nine gated
/// handlers, five predicates and the mask function, all byte-identical. So <c>BLOCKERS.md</c>
/// Trap E does not apply to this question, which is a thing to measure per question rather than
/// assume either way.
/// </para>
/// </remarks>
public class ChannelPlanTests
{
    [Fact]
    public void The_library_names_seven_categories()
    {
        Assert.Equal(7, SasChannelPlan.Categories.Count);
        Assert.Equal(
            ["general_control", "aft", "progressive", "legacy_bonus", "validation",
             "component_authentication", "information"],
            SasChannelPlan.Categories.Select(SasCategories.NameOf));
    }

    [Fact]
    public void Every_gated_command_is_a_command_the_catalogue_carries()
    {
        Assert.NotEmpty(SasCatalogue.CommandCategories);
        Assert.All(
            SasCatalogue.CommandCategories.Keys,
            code => Assert.True(SasCatalogue.LongPolls.ContainsKey(code), $"${code:X2}"));
    }

    [Fact]
    public void Every_filtered_exception_is_an_exception_the_catalogue_carries()
    {
        Assert.NotEmpty(SasCatalogue.ExceptionCategories);
        Assert.All(
            SasCatalogue.ExceptionCategories.Keys,
            code => Assert.True(SasCatalogue.Exceptions.ContainsKey(code), $"${code:X2}"));
    }

    /// <summary>
    /// The finding this wave exists to produce, held as a test so it cannot quietly stop being
    /// true.
    /// </summary>
    /// <remarks>
    /// The cashout ticket information poll carries no channel gate in the library at all, so a
    /// host on a channel that does not own validation is answered with the real validation number
    /// and the real amount. GSA 10.1.4 step 5 requires that host to be ignored or answered with
    /// zeros. If a later revision of the library adds the gate, this test fails and the
    /// conformance case starts passing — which is the right way round.
    /// </remarks>
    [Fact]
    public void The_cashout_ticket_information_poll_is_gated_on_no_category()
    {
        Assert.False(SasCatalogue.CommandCategories.ContainsKey(0x3D));
        Assert.False(SasCatalogue.ExceptionCategories.ContainsKey(0x3D));

        // Its neighbours in the same business are gated, which is what makes the absence a fact
        // about that one command rather than a table that reached nothing.
        Assert.Equal(SasCategory.Validation, SasCatalogue.CommandCategories[0x4D]);
        Assert.Equal(SasCategory.Validation, SasCatalogue.CommandCategories[0x57]);
        Assert.Equal(SasCategory.Validation, SasCatalogue.ExceptionCategories[0x67]);
    }

    [Fact]
    public void A_category_on_a_channel_with_no_poll_address_is_refused_with_a_reason()
    {
        bool made = SasChannelPlan.TryCreate(
            [SasAddress.Of(1), null, null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
            out _,
            out string? refusal);

        Assert.False(made);
        Assert.NotNull(refusal);
        Assert.Contains("validation is on channel 2, which has no poll address", refusal);
        Assert.Contains("reaches nobody", refusal);
    }

    [Fact]
    public void A_machine_with_no_address_on_any_channel_is_refused()
    {
        bool made = SasChannelPlan.TryCreate([null, null, null, null], new Dictionary<SasCategory, SasChannel>(),
            out _, out string? refusal);

        Assert.False(made);
        Assert.Contains("not running SAS at all", refusal);
    }

    [Fact]
    public void A_category_left_unnamed_stays_on_the_first_channel()
    {
        Assert.True(SasChannelPlan.TryCreate(
            [SasAddress.Of(1), SasAddress.Of(2), null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
            out SasChannelPlan plan,
            out _));

        Assert.Equal(SasChannel.Two, plan.OwnerOf(SasCategory.Validation));
        Assert.All(
            SasChannelPlan.Categories.Where(c => c != SasCategory.Validation),
            category => Assert.Equal(SasChannel.One, plan.OwnerOf(category)));

        Assert.Equal([SasChannel.One, SasChannel.Two], plan.Open);
        Assert.Equal(SasAddress.Of(2), plan.AddressOf(SasChannel.Two));
        Assert.Null(plan.AddressOf(SasChannel.Three));
    }

    [Fact]
    public void A_gated_command_is_answered_only_on_the_channel_that_owns_its_category()
    {
        SasChannelPlan plan = Split();

        Assert.False(plan.Answers(SasChannel.One, 0x70));
        Assert.True(plan.Answers(SasChannel.Two, 0x70));

        // And one in no category is answered on both, which is the finding above stated as
        // behaviour rather than as a table lookup.
        Assert.True(plan.Answers(SasChannel.One, 0x3D));
        Assert.True(plan.Answers(SasChannel.Two, 0x3D));
    }

    [Fact]
    public void A_filtered_exception_reaches_only_the_channel_that_owns_its_category()
    {
        SasChannelPlan plan = Split();

        Assert.False(plan.Hears(SasChannel.One, 0x67));
        Assert.True(plan.Hears(SasChannel.Two, 0x67));

        Assert.True(plan.Hears(SasChannel.One, 0x3D));
        Assert.True(plan.Hears(SasChannel.Two, 0x3D));
    }

    [Fact]
    public void A_channel_without_validation_reports_none_of_the_validation_feature_bits()
    {
        SasChannelPlan plan = Split();

        // Every bit set, so what survives the mask is what the channel is allowed to say.
        byte[] onChannelOne = [0xFF, 0xFF, 0xFF];
        plan.MaskFeatures(SasChannel.One, onChannelOne);

        // The four validation bits are the top nibble of the first byte - the extensions bit, the
        // two style bits and ticket redemption - and nothing below them belongs to validation.
        Assert.Equal(0x00, onChannelOne[0] & 0xF0);
        Assert.Equal(0x0F, onChannelOne[0] & 0x0F);

        byte[] onChannelTwo = [0xFF, 0xFF, 0xFF];
        plan.MaskFeatures(SasChannel.Two, onChannelTwo);
        Assert.Equal(0xF0, onChannelTwo[0] & 0xF0);
    }

    [Fact]
    public void A_channel_owning_everything_has_nothing_masked()
    {
        byte[] features = [0xFF, 0xFF, 0xFF];
        SasChannelPlan.Single.MaskFeatures(SasChannel.One, features);
        Assert.Equal<byte[]>([0xFF, 0xFF, 0xFF], features);
    }

    [Fact]
    public void The_arrangement_reads_as_one_line_of_provenance()
    {
        Assert.Equal(
            "channels 1@01 2@02 · general_control=1 aft=1 progressive=1 legacy_bonus=1 "
            + "validation=2 component_authentication=1 information=1",
            Split().Describe());
    }

    [Theory]
    [InlineData("validation=2", SasCategory.Validation, SasChannel.Two)]
    [InlineData("component_authentication=3", SasCategory.ComponentAuthentication, SasChannel.Three)]
    [InlineData("Aft=4", SasCategory.Aft, SasChannel.Four)]
    public void An_owner_is_read_the_way_a_report_writes_it(
        string text, SasCategory category, SasChannel channel)
    {
        Assert.True(SasChannelPlan.TryParseOwners(text, out Dictionary<SasCategory, SasChannel> owners, out _));
        Assert.Equal(channel, owners[category]);
    }

    [Fact]
    public void A_name_that_is_not_a_category_is_refused_with_the_names_that_are()
    {
        Assert.False(SasChannelPlan.TryParseOwners("cashout=2", out _, out string? error));
        Assert.Contains("is not a category", error);
        Assert.Contains("validation", error);
    }

    /// <summary>
    /// A case that declares two channels asks for two, so a machine that opens one skips it.
    /// </summary>
    private static SasChannelPlan Split()
    {
        Assert.True(SasChannelPlan.TryCreate(
            [SasAddress.Of(1), SasAddress.Of(2), null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two },
            out SasChannelPlan plan,
            out _));

        return plan;
    }

    /// <summary>
    /// A declaration says how many channels the case wants, and nothing claims to have them.
    /// </summary>
    /// <remarks>
    /// Deliberately not a requirement. No long poll reports how a machine divides itself, so
    /// nothing can answer that before the link opens; opening the second channel is the
    /// measurement, and a machine with one channel makes that step a skip naming the channel.
    /// </remarks>
    /// <summary>
    /// A command a bare two-byte poll cannot reach is one whose silence means nothing.
    /// </summary>
    /// <remarks>
    /// Two of the nine validation commands can be asked with nothing after the command byte. The
    /// rest are framed longer by the machine's own receiver, which drops a short one as incomplete
    /// before any handler sees it - so a case asserting the silence of one of those would be
    /// asserting the framing and reading it as the channel gate. Both cases written for this wave
    /// did exactly that until it was measured.
    /// </remarks>
    [Fact]
    public void Only_two_validation_commands_can_be_asked_with_nothing_after_the_command()
    {
        byte[] validation =
        [
            .. SasCatalogue.CommandCategories
                .Where(pair => pair.Value == SasCategory.Validation)
                .Select(pair => pair.Key)
                .Order(),
        ];

        Assert.Equal<byte[]>([0x4C, 0x4D, 0x57, 0x58, 0x70, 0x71, 0x7B, 0x7C, 0x7D], validation);

        byte[] reachable =
        [
            .. validation.Where(code =>
                SasCatalogue.LongPolls[code].HostMessageLength == 2),
        ];

        Assert.Equal<byte[]>([0x57, 0x70], reachable);
    }

    [Fact]
    public void A_declaration_says_how_many_channels_a_case_wants()
    {
        var declaration = new MachineChannelDeclaration(
            SourceLocation.None,
            [SasAddress.Of(1), SasAddress.Of(2), null, null],
            new Dictionary<SasCategory, SasChannel> { [SasCategory.Validation] = SasChannel.Two });

        Assert.Equal(2, declaration.Declared);
        Assert.Equal("channel.2", MachineChannelDeclaration.CapabilityFor(SasChannel.Two));
    }
}
