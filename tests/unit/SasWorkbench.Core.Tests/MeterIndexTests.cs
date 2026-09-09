using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// Which meter each command reports, which command each name is read from, and what the two
/// commands reporting one meter actually answered on a real gaming machine.
/// </summary>
/// <remarks>
/// <para>
/// Three waves put widening the meter index off with the same reason: the gaming machine library
/// reads a meter two ways and the two do not always answer the same number, so moving a name to
/// the wrong command would quietly change what an existing test measures. This file is where that
/// reason is discharged rather than repeated.
/// </para>
/// <para>
/// Nothing below asserts a fact about the library that was copied here by hand. Which meters a
/// command reports, and whether it goes through the helper that divides them, are generated from
/// the library's own handlers into <see cref="SasCatalogue.MeterReads"/>; which meters that helper
/// leaves alone is generated from the predicate that decides it. The layouts are checked against
/// both.
/// </para>
/// </remarks>
public class MeterIndexTests
{
    /// <summary>
    /// The sixteen names a script could read before this wave, and the command each came from.
    /// </summary>
    /// <remarks>
    /// Copied from <c>sasctl catalogue readable-meters</c> as it stood before the index was
    /// widened. Adding twenty-four names must not move any of these: a case that has always read
    /// <c>bills_20</c> from long poll <c>$1E</c> would otherwise start reading it from <c>$35</c>,
    /// which on a machine in either default meter mode is a different number.
    /// </remarks>
    public static TheoryData<string, byte> NamesThatExistedBefore => new()
    {
        { "bills_1", 0x1E },
        { "bills_5", 0x1E },
        { "bills_10", 0x1E },
        { "bills_20", 0x1E },
        { "bills_50", 0x1E },
        { "bills_100", 0x1E },
        { "current_credits", 0x1A },
        { "games_lost", 0x17 },
        { "games_played", 0x15 },
        { "games_won", 0x16 },
        { "total_bill_value", 0x20 },
        { "total_cancelled_credits", 0x10 },
        { "total_coin_in", 0x11 },
        { "total_coin_out", 0x12 },
        { "total_drop", 0x13 },
        { "total_jackpot", 0x14 },
    };

    [Theory]
    [MemberData(nameof(NamesThatExistedBefore))]
    public void Widening_the_index_moved_no_name_that_was_already_in_it(string name, byte command)
    {
        Assert.True(MeterIndex.TryResolve(name, out MeterSource source));
        Assert.Equal(command, source.Command);
    }

    [Fact]
    public void The_index_is_wider_than_it_was_and_still_holds_all_sixteen()
    {
        Assert.Equal(40, MeterIndex.All.Count);
        Assert.Equal(16, NamesThatExistedBefore.Count);
    }

    /// <summary>
    /// The layouts and the library agree about which meters each reply carries, and in what order.
    /// </summary>
    /// <remarks>
    /// This is the check that makes every meter code in <c>SasLayouts</c> a claim somebody can
    /// verify rather than take on trust. A field given the wrong meter fails here, and so does a
    /// reply that grows a field the library does not report.
    /// </remarks>
    [Fact]
    public void Every_layout_reports_the_meters_the_library_says_that_command_reports()
    {
        foreach (SasMessageLayout layout in SasLayouts.All.Values)
        {
            // $20 is the one reply whose number the machine computes: its handler reads
            // twenty-four bill counts and adds each multiplied by its face value, so the library
            // names twenty-four meters where the reply has one field and no meter code.
            if (layout.Command == 0x20)
                continue;

            ushort[] declared = layout.Response
                .Where(f => f.Meter is not null)
                .Select(f => (ushort)f.Meter!.Value)
                .ToArray();

            if (declared.Length == 0)
                continue;

            Assert.True(SasCatalogue.MeterReads.TryGetValue(layout.Command, out SasMeterReadInfo? reads),
                $"${layout.Command:X2} names meters the library's handler does not read");
            Assert.Equal(reads.Meters, declared);
        }
    }

    /// <summary>
    /// The one reply that names meters in the library and carries none of its own.
    /// </summary>
    [Fact]
    public void The_total_value_of_notes_is_computed_from_twenty_four_meters_and_names_none()
    {
        SasMessageLayout layout = SasLayouts.For(0x20)!;
        SasField field = layout.Response.Single();

        Assert.True(field.IsMeter);
        Assert.Null(field.Meter);
        Assert.Equal(24, SasCatalogue.MeterReads[0x20].Meters.Count);

        Assert.True(MeterIndex.TryResolve("total_bill_value", out MeterSource source));
        Assert.Null(source.Meter);
    }

    /// <summary>
    /// Whether a name's reading is scaled is the library's answer, not one written down here.
    /// </summary>
    [Fact]
    public void Whether_a_reading_is_scaled_comes_from_the_generated_catalogue()
    {
        foreach (MeterSource source in MeterIndex.All)
        {
            bool throughHelper = SasCatalogue.MeterReads.TryGetValue(source.Command, out SasMeterReadInfo? reads)
                                 && reads.ThroughSharedHandler;
            bool multigame = source.Meter is not null
                             && SasCatalogue.MultigameMeters.Contains((ushort)source.Meter.Value);

            Assert.Equal(throughHelper && source.Meter is not null && !multigame,
                source.ScaledByAccountingDenomination);
        }
    }

    /// <summary>
    /// A count is read from a command that does not divide it, wherever one exists.
    /// </summary>
    /// <remarks>
    /// The library divides by the accounting denomination in cents, which converts a value in
    /// cents into accounting units and converts a number of notes into nothing. So where two
    /// commands report a count, the name takes the one that leaves it alone; where only one does,
    /// the name takes it and says that it is scaled.
    /// </remarks>
    [Fact]
    public void A_count_is_read_from_a_command_that_leaves_it_alone_where_there_is_one()
    {
        foreach (MeterSource chosen in MeterIndex.All.Where(m => m.Unit == SasUnit.Count))
        {
            IReadOnlyList<MeterSource> all = MeterIndex.SourcesFor(chosen.Name);
            bool unscaledExists = all.Any(s => !s.ScaledByAccountingDenomination);

            Assert.Equal(!unscaledExists, chosen.ScaledByAccountingDenomination);
        }
    }

    /// <summary>
    /// A credit value is read from the command that does divide it, wherever one exists.
    /// </summary>
    /// <remarks>
    /// The other half of the same rule, and the library's own comment is the reason: it says
    /// meters expressed in credit should be divided by the denomination. So for those the divided
    /// reading is the corrected one and the bulk replies are the uncorrected one.
    /// </remarks>
    [Fact]
    public void A_credit_value_is_read_from_the_command_that_converts_it_where_there_is_one()
    {
        foreach (MeterSource chosen in MeterIndex.All.Where(m => m.Unit == SasUnit.Credits))
        {
            IReadOnlyList<MeterSource> all = MeterIndex.SourcesFor(chosen.Name);
            bool scaledExists = all.Any(s => s.ScaledByAccountingDenomination);

            Assert.Equal(scaledExists, chosen.ScaledByAccountingDenomination);
        }
    }

    /// <summary>
    /// Every readable meter is answered by a command a runner can send with nothing in it.
    /// </summary>
    /// <remarks>
    /// A meter reading sends the command bare, so a reply that has to be asked for in a shape —
    /// a game number, a validation type, a wager category — cannot be one. Those fields are still
    /// assertable on their own reply, which is what <c>host expect field</c> is for.
    /// </remarks>
    [Fact]
    public void Every_readable_meter_comes_from_a_reply_that_needs_no_request()
    {
        foreach (MeterSource source in MeterIndex.All)
        {
            SasMessageLayout layout = SasLayouts.All[source.Command];
            Assert.Empty(layout.Request);
            Assert.Equal(SasResponseShape.Data, layout.Shape);
            Assert.Contains(layout.Response, f => f.Name == source.Field && f.IsMeter);
        }
    }

    /// <summary>
    /// The per-game replies report meters and offer no name, and say which meter they report.
    /// </summary>
    [Fact]
    public void A_per_game_reply_reports_a_meter_and_offers_no_name()
    {
        foreach (byte command in (byte[])[0x2D, 0x52])
        {
            SasMessageLayout layout = SasLayouts.For(command)!;
            SasField[] meters = layout.Response.Where(f => f.Meter is not null).ToArray();

            Assert.NotEmpty(meters);
            Assert.All(meters, f => Assert.False(f.IsMeter));
        }
    }

    /// <summary>
    /// Two names, one meter: the coin acceptor and the external coin acceptor cannot disagree.
    /// </summary>
    /// <remarks>
    /// Long polls <c>$2A</c> and <c>$47</c> both read <c>TotalCreditsFromCoinAcceptor</c> in the
    /// library, so a test written against one of them is written against both. Recorded rather
    /// than modelled around, and asserted so that a library that split them later shows up here.
    /// </remarks>
    [Fact]
    public void The_coin_acceptor_and_the_external_coin_acceptor_are_one_meter()
    {
        Assert.True(MeterIndex.TryResolve("true_coin_in", out MeterSource coin));
        Assert.True(MeterIndex.TryResolve("external_coin_acceptor_credits", out MeterSource external));

        Assert.Equal(SasMeterCode.TotalCreditsFromCoinAcceptor, coin.Meter);
        Assert.Equal(coin.Meter, external.Meter);
        Assert.NotEqual(coin.Command, external.Command);
    }

    /// <summary>
    /// A credit meter reported by a dedicated command and by a bulk one is read from the
    /// dedicated one.
    /// </summary>
    /// <remarks>
    /// This used to be stated as a general preference for a dedicated command over a bulk one,
    /// and it is not one: for a count the preference runs the other way, because a count read
    /// through the dedicated command is divided by a currency unit. Credits wagered is a
    /// multigame meter, which the helper leaves alone, so here the two agree and the tie is
    /// broken on the reply carrying one number instead of six.
    /// </remarks>
    [Fact]
    public void A_credit_meter_is_read_from_the_command_that_answers_it_alone()
    {
        Assert.True(MeterIndex.TryResolve("total_coin_in", out MeterSource source));

        Assert.Equal(0x11, source.Command);
        Assert.False(source.ScaledByAccountingDenomination);
        Assert.Equal(4, MeterIndex.SourcesFor("total_coin_in").Count);
    }

    /// <summary>
    /// A count reported by a dedicated command and by a bulk one is read from the bulk one.
    /// </summary>
    [Fact]
    public void A_count_is_read_from_the_bulk_reply_even_though_a_dedicated_command_exists()
    {
        Assert.True(MeterIndex.TryResolve("bills_20", out MeterSource source));

        Assert.Equal(0x1E, source.Command);
        Assert.Equal("bills_20", source.Field);
        Assert.False(source.ScaledByAccountingDenomination);

        MeterSource dedicated = MeterIndex.SourcesFor("bills_20")[1];
        Assert.Equal(0x35, dedicated.Command);
        Assert.True(dedicated.ScaledByAccountingDenomination);
    }

    [Fact]
    public void Friendly_names_resolve_to_a_real_field()
    {
        Assert.True(MeterIndex.TryResolve("total_bill_in", out MeterSource source));

        Assert.Equal("total_bill_value", source.Field);
        Assert.Equal(0x20, source.Command);
    }

    [Fact]
    public void A_meter_no_layout_returns_is_not_readable()
    {
        Assert.False(MeterIndex.TryResolve("total_widgets", out _));
    }

    /// <summary>
    /// The two counts long poll <c>$18</c> answers are meters, which this build used to deny.
    /// </summary>
    /// <remarks>
    /// The note it carried said the protocol keeps no code for either. The library's own handler
    /// reads them by code — <c>GamesSinceLastReset</c> and <c>GamesSinceSlotDoorClosure</c> — so
    /// a selected-meter poll can ask for either, and a real machine answered both when asked.
    /// </remarks>
    [Fact]
    public void The_two_counts_a_power_up_and_a_door_closure_restart_are_meters()
    {
        Assert.True(MeterIndex.TryResolve("games_since_power_up", out MeterSource powerUp));
        Assert.True(MeterIndex.TryResolve("games_since_door_closure", out MeterSource door));

        Assert.Equal(SasMeterCode.GamesSinceLastReset, powerUp.Meter);
        Assert.Equal(SasMeterCode.GamesSinceSlotDoorClosure, door.Meter);
        Assert.Equal(0x18, powerUp.Command);
        Assert.Equal(0x18, door.Command);
    }
}
