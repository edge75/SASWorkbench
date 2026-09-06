using SasWorkbench.Core.Tickets;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The contract between this program and the peripheral simulator, which implements the same one
/// ticket model in C++ and reads these very files.
/// </summary>
/// <remarks>
/// <para>
/// These specimens are the shared definition of both encodings. The simulator's own tests read the
/// same bytes and assert the same values, so a change made on one side and not the other fails a
/// test here or there rather than producing a ticket the far end silently misreads.
/// </para>
/// <para>
/// The byte-for-byte assertions are the point of the exercise, so the fixtures are pinned to LF in
/// .gitattributes. Without that a Windows checkout would rewrite them and the two implementations
/// would be compared against different files.
/// </para>
/// </remarks>
public class TicketFixtureTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "tickets", name);

    private static Ticket Full() => new()
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
        Note = "every field set, so a dropped one shows",
    };

    [Theory]
    [InlineData("full.ticket")]
    [InlineData("full.json")]
    public void Reads_the_shared_specimen(string name)
    {
        Assert.Equal(Full(), TicketFile.Read(FixturePath(name)));
    }

    [Theory]
    [InlineData("bare.ticket")]
    [InlineData("bare.json")]
    public void Reads_the_shared_specimen_of_a_ticket_with_nothing_set(string name)
    {
        Assert.Equal(new Ticket(), TicketFile.Read(FixturePath(name)));
    }

    /// <summary>
    /// Writing must reproduce the specimen exactly. This is what makes the file a contract rather
    /// than a sample: if either program's output drifts by so much as a space, it is caught here
    /// instead of in whatever downstream thing was comparing tickets.
    /// </summary>
    [Theory]
    [InlineData("full.ticket", TicketEncoding.KeyValue)]
    [InlineData("full.json", TicketEncoding.Json)]
    [InlineData("bare.ticket", TicketEncoding.KeyValue)]
    [InlineData("bare.json", TicketEncoding.Json)]
    public void Writes_the_shared_specimen_byte_for_byte(string name, TicketEncoding encoding)
    {
        string path = FixturePath(name);
        Ticket ticket = TicketFile.Read(path);

        string written = TicketFile.Render(ticket, encoding);

        Assert.Equal(File.ReadAllText(path), written);
    }

    /// <summary>
    /// A ticket is exchanged between two programs on two operating systems, so its line endings are
    /// part of the file rather than a local convention. A writer following the platform's newline
    /// would make the same ticket a different sequence of bytes on Windows and on Linux, and every
    /// byte-for-byte check above would then pass only on the machine that wrote it.
    /// </summary>
    [Theory]
    [InlineData(TicketEncoding.KeyValue)]
    [InlineData(TicketEncoding.Json)]
    public void Writes_the_same_line_endings_on_every_platform(TicketEncoding encoding)
    {
        string written = TicketFile.Render(Full(), encoding);

        Assert.DoesNotContain('\r', written);
        Assert.Contains('\n', written);
    }
}
