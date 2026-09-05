using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Tests;

public class TicketTests
{
    // A ticket with every field set to something distinguishable, so a field dropped or swapped by
    // an encoding shows up rather than matching a default by luck. The validation number is a full
    // sixteen digits on purpose: that is what secure enhanced validation produces, and it is past
    // 2^53, which is where a JSON reader backed by a double starts rounding.
    private static Ticket Specimen() => new()
    {
        ValidationNumber = 9876543210987654,
        ValidationType = TicketValidationType.RestrictedPromotionalFromCashout,
        AmountCents = 2000,
        TicketNumber = 17,
        PoolId = 3,
        Expiration = new DateOnly(2026, 12, 31),
        ValidationSystemId = 0x40,
        PrintedAt = new DateTime(2026, 9, 6, 14, 23, 11),
        AssetNumber = 1234,
        Location = "Quixant Test Floor",
        Address1 = "1 Somewhere Street",
        Address2 = "Cambridge",
        Redeemed = true,
        Note = "hand-written for the expired case",
    };

    /// <summary>
    /// The invariant the whole two-encoding decision rests on: a ticket converted one way and back
    /// is the same ticket. If this ever fails, the two encodings have become two models.
    /// </summary>
    [Theory]
    [InlineData(TicketEncoding.KeyValue)]
    [InlineData(TicketEncoding.Json)]
    public void Survives_a_round_trip_through_either_encoding(TicketEncoding encoding)
    {
        Ticket original = Specimen();

        Ticket returned = TicketFile.Parse(TicketFile.Render(original, encoding), encoding);

        Assert.Equal(original, returned);
    }

    /// <summary>
    /// The stronger form of the same rule: a ticket that goes out as text, comes back, goes out as
    /// JSON and comes back again is still the same ticket. This is the path a person actually
    /// takes when they export one, edit it by hand and load it again.
    /// </summary>
    [Fact]
    public void Survives_a_round_trip_through_both_encodings_in_turn()
    {
        Ticket original = Specimen();

        Ticket viaText = TicketFile.Parse(TicketFile.Render(original, TicketEncoding.KeyValue), TicketEncoding.KeyValue);
        Ticket viaJson = TicketFile.Parse(TicketFile.Render(viaText, TicketEncoding.Json), TicketEncoding.Json);
        Ticket andBack = TicketFile.Parse(TicketFile.Render(viaJson, TicketEncoding.KeyValue), TicketEncoding.KeyValue);

        Assert.Equal(original, andBack);
    }

    /// <summary>
    /// One model, not two: whichever encoding a ticket arrives in, the ticket is the same object.
    /// </summary>
    [Fact]
    public void Reads_the_same_ticket_from_either_encoding()
    {
        Ticket original = Specimen();

        Ticket fromText = TicketFile.Parse(TicketFile.Render(original, TicketEncoding.KeyValue), TicketEncoding.KeyValue);
        Ticket fromJson = TicketFile.Parse(TicketFile.Render(original, TicketEncoding.Json), TicketEncoding.Json);

        Assert.Equal(fromText, fromJson);
    }

    /// <summary>
    /// The guard against the two encodings drifting apart. Both are generated from one field table,
    /// so this checks the property that makes that worth doing: every field of the model appears,
    /// by the same name, in both files.
    /// </summary>
    [Fact]
    public void Every_field_appears_in_both_encodings_under_the_same_name()
    {
        string text = TicketFile.Render(Specimen(), TicketEncoding.KeyValue);
        string json = TicketFile.Render(Specimen(), TicketEncoding.Json);

        Assert.NotEmpty(TicketFields.All);
        foreach (TicketField field in TicketFields.All)
        {
            Assert.Contains(field.Name, text, StringComparison.Ordinal);
            Assert.Contains($"\"{field.Name}\"", json, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The reason the validation number is a string in JSON. Sixteen digits is what secure enhanced
    /// validation produces and it is past 2^53, so a reader that took it as a JSON number would
    /// round it. Written as text it comes back exactly, digit for digit.
    /// </summary>
    [Fact]
    public void Keeps_a_sixteen_digit_validation_number_exact_in_json()
    {
        var original = new Ticket { ValidationNumber = 9007199254740993 };

        string json = TicketFile.Render(original, TicketEncoding.Json);
        Ticket returned = TicketFile.Parse(json, TicketEncoding.Json);

        Assert.Contains("\"9007199254740993\"", json, StringComparison.Ordinal);
        Assert.Equal(9007199254740993UL, returned.ValidationNumber);
    }

    /// <summary>
    /// The text encoding is what a person edits, so it accepts what a person plausibly writes:
    /// comments either way round, no section header, keys in any case and any order, spacing to
    /// taste, and a missing field left at its default.
    /// </summary>
    [Fact]
    public void Accepts_a_hand_written_file()
    {
        string text = """
            ; an expired ticket, written by hand
            # either comment marker is fine
            Amount_Cents=2500
            validation_number  =   1234567890
            expiration = 2020-01-01
            validation_type = restricted_promotional_from_cashout
            """;

        Ticket ticket = TicketFile.Parse(text, TicketEncoding.KeyValue);

        Assert.Equal(2500, ticket.AmountCents);
        Assert.Equal(1234567890UL, ticket.ValidationNumber);
        Assert.Equal(new DateOnly(2020, 1, 1), ticket.Expiration);
        Assert.Equal(TicketValidationType.RestrictedPromotionalFromCashout, ticket.ValidationType);
        Assert.Equal(0u, ticket.TicketNumber);
        Assert.Null(ticket.PrintedAt);
    }

    /// <summary>The wire codes are what a specification-shaped case names, so they are accepted too.</summary>
    [Theory]
    [InlineData("0x04", TicketValidationType.DebitFromTransfer)]
    [InlineData("128", TicketValidationType.CashableRedeemed)]
    [InlineData("CashableFromTransfer", TicketValidationType.CashableFromTransfer)]
    [InlineData("cashable_from_transfer", TicketValidationType.CashableFromTransfer)]
    public void Accepts_a_validation_type_written_any_of_the_ways_a_person_writes_it(
        string written, TicketValidationType expected)
    {
        Ticket ticket = TicketFile.Parse($"validation_type = {written}", TicketEncoding.KeyValue);

        Assert.Equal(expected, ticket.ValidationType);
    }

    /// <summary>
    /// A field name nobody knows is a typo in a hand-written file far more often than it is an
    /// intention, and accepting it silently would hand back a ticket that is not the one on the page.
    /// </summary>
    [Fact]
    public void Refuses_a_field_it_does_not_know_rather_than_ignoring_it()
    {
        var thrown = Assert.Throws<TicketFormatException>(
            () => TicketFile.Parse("amuont_cents = 2000", TicketEncoding.KeyValue));

        Assert.Contains("amuont_cents", thrown.Reason, StringComparison.Ordinal);
        Assert.Contains("line 1", thrown.Reason, StringComparison.Ordinal);
    }

    /// <summary>Which of two values was meant is unknowable, so neither is chosen.</summary>
    [Fact]
    public void Refuses_a_field_given_twice()
    {
        var thrown = Assert.Throws<TicketFormatException>(
            () => TicketFile.Parse("amount_cents = 2000\namount_cents = 3000", TicketEncoding.KeyValue));

        Assert.Contains("amount_cents", thrown.Reason, StringComparison.Ordinal);
    }

    /// <summary>A JSON file naming a field nobody knows is refused for the same reason.</summary>
    [Fact]
    public void Refuses_an_unknown_field_in_json_too()
    {
        var thrown = Assert.Throws<TicketFormatException>(
            () => TicketFile.Parse("""{"amount_cents": 2000, "colour": "blue"}""", TicketEncoding.Json));

        Assert.Contains("colour", thrown.Reason, StringComparison.Ordinal);
    }

    /// <summary>A refusal says which line and what was wrong with it, because a person is reading it.</summary>
    [Fact]
    public void Names_the_line_and_the_value_when_a_value_will_not_parse()
    {
        var thrown = Assert.Throws<TicketFormatException>(
            () => TicketFile.Parse("amount_cents = 2000\nexpiration = 31/12/2026", TicketEncoding.KeyValue));

        Assert.Contains("line 2", thrown.Reason, StringComparison.Ordinal);
        Assert.Contains("31/12/2026", thrown.Reason, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", thrown.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whitespace inside a printed line is part of what the ticket says, so it has to survive a
    /// round trip even though a classic key/value file trims values.
    /// </summary>
    [Fact]
    public void Keeps_awkward_whitespace_and_newlines_in_text()
    {
        var original = new Ticket { Location = "  spaced  ", Note = "two\nlines\twith a tab" };

        Ticket returned = TicketFile.Parse(
            TicketFile.Render(original, TicketEncoding.KeyValue), TicketEncoding.KeyValue);

        Assert.Equal("  spaced  ", returned.Location);
        Assert.Equal("two\nlines\twith a tab", returned.Note);
    }

    /// <summary>An unset date is absent, not an empty string claiming a date was written.</summary>
    [Fact]
    public void Writes_an_unset_date_as_null_in_json()
    {
        string json = TicketFile.Render(new Ticket(), TicketEncoding.Json);

        Assert.Contains("\"expiration\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"printed_at\": null", json, StringComparison.Ordinal);
    }

    /// <summary>The encoding is taken from the extension, both ways.</summary>
    [Theory]
    [InlineData("wallet/000017.json", TicketEncoding.Json)]
    [InlineData("wallet/000017.ticket", TicketEncoding.KeyValue)]
    [InlineData("expired.ini", TicketEncoding.KeyValue)]
    [InlineData("expired.TXT", TicketEncoding.KeyValue)]
    public void Chooses_the_encoding_from_the_extension(string path, TicketEncoding expected)
    {
        Assert.Equal(expected, TicketFile.EncodingOf(path));
    }

    /// <summary>
    /// An extension nobody recognises is refused rather than guessed at, and the refusal says what
    /// this build does recognise so the next attempt succeeds.
    /// </summary>
    [Fact]
    public void Refuses_an_extension_it_cannot_place()
    {
        var thrown = Assert.Throws<TicketFormatException>(() => TicketFile.EncodingOf("ticket.yaml"));

        Assert.Contains(".json", thrown.Reason, StringComparison.Ordinal);
        Assert.Contains(".ticket", thrown.Reason, StringComparison.Ordinal);
    }

    /// <summary>A ticket written to a file and read back off disk is the same ticket.</summary>
    [Theory]
    [InlineData(TicketEncoding.KeyValue)]
    [InlineData(TicketEncoding.Json)]
    public void Survives_a_round_trip_through_a_file(TicketEncoding encoding)
    {
        Ticket original = Specimen();
        string directory = Path.Combine(Path.GetTempPath(), "sasworkbench-tickets", Path.GetRandomFileName());
        string path = Path.Combine(directory, "000017" + TicketFile.ExtensionFor(encoding));

        try
        {
            TicketFile.Write(path, original);

            Assert.Equal(original, TicketFile.Read(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
