using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>How the fake gaming machine identifies and configures itself.</summary>
/// <remarks>
/// The defaults are the values a real machine reported over a real link, so a test written
/// against the fake machine sees the same identification reply it would see against the real
/// one. That makes the two comparable, which is the whole reason the fake exists.
/// </remarks>
public sealed record FakeEgmOptions
{
    /// <summary>The poll address this machine answers to.</summary>
    public SasAddress Address { get; init; } = SasAddress.Of(1);

    /// <summary>Which channel this machine is answering on.</summary>
    public SasChannel Channel { get; init; } = SasChannel.One;

    /// <summary>Two-character manufacturer identifier.</summary>
    public string GameId { get; init; } = "QX";

    /// <summary>Three-character machine identifier.</summary>
    public string AdditionalId { get; init; } = "001";

    /// <summary>
    /// Accounting denomination code, which is also the unit every credit value is counted in.
    /// </summary>
    /// <remarks>
    /// The default is what a real machine published over a real link: code 02, five cents. A
    /// fixture that published one unit and counted in another would disagree with that machine on
    /// every credit meter while looking healthy, which is the one disagreement between the two
    /// peers that means nothing.
    /// </remarks>
    public byte Denomination { get; init; } = 0x02;

    /// <summary>Largest bet the machine accepts, in credits.</summary>
    public byte MaxBet { get; init; } = 0xFF;

    /// <summary>Progressive group the machine belongs to.</summary>
    public byte ProgressiveGroup { get; init; }

    /// <summary>Machine option bits.</summary>
    public ushort GameOptions { get; init; }

    /// <summary>Paytable identifier, up to six characters.</summary>
    public string PaytableId { get; init; } = "001";

    /// <summary>
    /// What this machine calls its game, which only the extended game information reply carries.
    /// </summary>
    /// <remarks>
    /// Fixture configuration, of a piece with the serial number and the paytable identifier above:
    /// a name is something a machine is given rather than machinery it has. An empty name is
    /// legitimate and the reply says so with a zero length, which is what a machine that was never
    /// given one answers.
    /// </remarks>
    public string GameName { get; init; } = "QXFAKE";

    /// <summary>What one token is worth, as a Table C-4 code.</summary>
    /// <remarks>
    /// Zero, because this machine has no coin acceptor and no token, so nothing here ever
    /// configured one - and zero is the byte the real machine under test published for the same
    /// reason when the catalogue was swept on 2026-09-08.
    /// </remarks>
    public byte TokenDenomination { get; init; }

    /// <summary>Theoretical payback percentage, in hundredths of a percent.</summary>
    public int BasePercentage { get; init; } = 9210;

    /// <summary>Protocol revision this machine claims, as three digits.</summary>
    public string SasVersion { get; init; } = "602";

    /// <summary>Machine serial number.</summary>
    public string SerialNumber { get; init; } = "QXFAKE0001";

    /// <summary>
    /// The accounting denomination this machine is configured with, as a Table C-4 code.
    /// </summary>
    /// <remarks>
    /// Left unset, the machine behaves as it always has and reports whatever
    /// <see cref="Denomination"/> says, which keeps every test written before this option existed
    /// meaning what it meant. Set, it becomes the unit every credit value on the wire is counted
    /// in, it is what the identification reply publishes, and a value that section 16 does not
    /// allow is refused in favour of one cent rather than used.
    /// </remarks>
    public byte? AccountingDenomination { get; init; }

    /// <summary>
    /// Which of the machine library's five meter modes this machine is in.
    /// </summary>
    /// <remarks>
    /// The default is the mode the gaming machine under test is actually in: the platform above
    /// the library sets full manual at startup and writes it to memory. That matters, because in
    /// full manual the library never divides a meter by the accounting denomination, so on that
    /// machine the single-meter commands and the bulk replies cannot disagree however the
    /// denomination is set. A case that wants to see them disagree asks for one of the two default
    /// modes, and it is the only way to see it here.
    /// </remarks>
    public SasMeterMode MeterMode { get; init; } = SasMeterMode.FullManual;

    /// <summary>
    /// The denominations a player may choose, in cents.
    /// </summary>
    /// <remarks>
    /// Section 16 requires every one of them to divide evenly by the accounting denomination, and
    /// a machine configured otherwise must refuse the accounting denomination rather than report
    /// meters in a unit none of its denominations is a whole number of. Empty means the rule has
    /// nothing to check against and any expressible denomination is accepted.
    /// </remarks>
    public IReadOnlyList<int> PlayerDenominationsInCents { get; init; } = [];

    /// <summary>
    /// What one game credit is worth in cents, which is what a wager is counted in.
    /// </summary>
    /// <remarks>
    /// Distinct from the accounting denomination on purpose. A wager is a number of game credits
    /// and does not move when the accounting denomination does, while the coin in meter it feeds
    /// is in accounting units - and the two being confused is exactly the defect this machine
    /// exists to let a test catch.
    /// </remarks>
    public int CentsPerGameCredit { get; init; } = 100;

    /// <summary>
    /// Which validation style this machine performs when it cashes out.
    /// </summary>
    /// <remarks>
    /// Standard, because that is what a machine on fresh memory is in and what the cashout
    /// ticket information poll is defined against. The enhanced styles are named here so a test
    /// can build a machine that refuses that poll, which is the behaviour section 15.5 asks for
    /// and the one a script has no other way to see.
    /// </remarks>
    public SasCashoutValidation CashoutValidation { get; init; } = SasCashoutValidation.Standard;

    /// <summary>
    /// Commands this machine will not answer at all.
    /// </summary>
    /// <remarks>
    /// Machines differ in what they support, and a command a machine does not support draws
    /// silence rather than a refusal — a refusal would say the machine understood and declined.
    /// A test needs to be able to build such a machine, because how a host behaves when a poll
    /// it depends on goes unanswered is a real question with a wrong answer available.
    /// </remarks>
    public IReadOnlyCollection<byte> UnansweredCommands { get; init; } = [];

    /// <summary>
    /// How many coins are in the hopper. Machine state rather than a meter, because the library
    /// keeps no meter code for it.
    /// </summary>
    public int HopperLevel { get; init; }

    /// <summary>
    /// Which currency this machine's note acceptor reports, as the protocol's own country code.
    /// </summary>
    /// <remarks>
    /// Thirty-seven is the United States dollar in the platform's own table, and it is the default
    /// for the same reason every other default here is what it is: the machine this fixture stands
    /// in for was measured reporting it. Only the real-time note event carries this, so it is
    /// invisible to a host that has not asked for reporting.
    /// </remarks>
    public byte BillCountryCode { get; init; } = 37;

    /// <summary>
    /// How long the machine takes to answer, so a test can exercise a slow machine on purpose.
    /// </summary>
    public TimeSpan ResponseDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// How the machine is divided between hosts, or null for one channel owning everything.
    /// </summary>
    /// <remarks>
    /// A property of the machine rather than of a host, which is why it is configured here and
    /// read off the wire by everything else. A machine given no arrangement puts every category
    /// on the one channel it opens, which is what a single-host machine is.
    /// </remarks>
    public SasChannelPlan? Channels { get; init; }

    /// <summary>
    /// Why the arrangement asked for was refused, when a caller resolved one and it would not
    /// stand.
    /// </summary>
    /// <remarks>
    /// Carried through so a run can report a machine that would not take the configuration it was
    /// given. The refusal belongs to the machine, so the reason travels with the machine rather
    /// than being reconstructed by whoever reads the report.
    /// </remarks>
    public string? ChannelRefusal { get; init; }
}
