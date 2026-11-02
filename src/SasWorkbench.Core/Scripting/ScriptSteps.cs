using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>Where a step came from, so a failure can name the line a person wrote.</summary>
/// <param name="File">The script file.</param>
/// <param name="Line">One-based line number.</param>
public sealed record SourceLocation(string File, int Line)
{
    /// <summary>A location for something not read from a file.</summary>
    public static SourceLocation None { get; } = new("<none>", 0);

    public override string ToString() => $"{File}:{Line}";
}

/// <summary>What a test, or one assertion in it, came to.</summary>
public enum Verdict
{
    /// <summary>The assertion held.</summary>
    Pass,

    /// <summary>The assertion did not hold. Something under test is wrong.</summary>
    Fail,

    /// <summary>
    /// The environment could not run it. Never a pass and never a failure: failing a test for an
    /// environmental reason trains people to ignore red.
    /// </summary>
    Skip,

    /// <summary>The test itself, or the runner, broke.</summary>
    Error,
}

/// <summary>Which link a test asks for.</summary>
public enum ScriptTransport
{
    Loopback,
    Tcp,
    Serial,
}

/// <summary>How a meter assertion compares.</summary>
public enum MeterComparison
{
    /// <summary>Risen by exactly this much since the baseline.</summary>
    IncreasedBy,

    /// <summary>Risen by at least this much since the baseline.</summary>
    IncreasedByAtLeast,

    /// <summary>Unchanged since the baseline.</summary>
    Unchanged,

    /// <summary>Exactly this value.</summary>
    Equals,

    AtLeast,

    AtMost,
}

/// <summary>Where stimulus for the gaming machine comes from.</summary>
public enum StimulusBackend
{
    /// <summary>
    /// Nothing drives the machine. Steps that need stimulus are skipped with a reason; they are
    /// never quietly treated as satisfied.
    /// </summary>
    None,

    /// <summary>A person is asked to do it and to confirm when it is done.</summary>
    Manual,

    /// <summary>The peripheral simulator is driven directly, unattended.</summary>
    Qpm,

    /// <summary>The software gaming machine peer in this repository.</summary>
    Software,
}

/// <summary>One step of a test.</summary>
public abstract record ScriptStep(SourceLocation Location)
{
    /// <summary>One line describing what this step does, for a report.</summary>
    public abstract string Describe();
}

/// <summary>Opens the link to the machine under test.</summary>
public sealed record ConnectStep(
    SourceLocation Location,
    ScriptTransport Transport,
    string Target,
    SasAddress Address,
    SasChannel Channel) : ScriptStep(Location)
{
    /// <summary>
    /// Every machine this host polls, in the order the round visits them.
    /// </summary>
    /// <remarks>
    /// Empty for a host bound to one machine, which is what <see cref="Address"/> alone means and
    /// what every case written before rounds existed says. A round names its machines as ranges
    /// and lists, because a hundred of them written on a hundred lines is not a script anybody
    /// reads.
    /// </remarks>
    public IReadOnlyList<SasAddress> Round { get; init; } = [];

    public override string Describe()
    {
        string machines = Round.Count > 1
            ? $"addresses {SasAddressRound.Describe(Round)}"
            : $"address {Address}";

        return $"connect {Transport.ToString().ToLowerInvariant()} {Target} {machines} "
               + $"channel {(int)Channel}";
    }
}

/// <summary>
/// Moves the host to another machine in its round, for the steps that follow.
/// </summary>
/// <remarks>
/// The address twin of <see cref="UseChannelStep"/>, and it exists for the same reason: a floor
/// case is one sequence — read this machine's meters, move to that one, read its meters, compare —
/// and two scripts could not express "the other machine on the same line" at all.
/// </remarks>
public sealed record UseMachineStep(SourceLocation Location, SasAddress Address)
    : ScriptStep(Location)
{
    public override string Describe() => $"use machine {Address}";
}

/// <summary>
/// Moves the host to another channel of the same machine, for the steps that follow.
/// </summary>
/// <remarks>
/// A step rather than a second script, because the conformance case this exists for is written as
/// one sequence: print a ticket on the validating channel, move the host to the channel that does
/// not own validation, ask again there, move back. Two scripts could not express "the same ticket,
/// read from the other host" at all.
/// </remarks>
public sealed record UseChannelStep(SourceLocation Location, SasChannel Channel) : ScriptStep(Location)
{
    public override string Describe() => $"use channel {(int)Channel}";
}

/// <summary>Sets how often the host polls.</summary>
public sealed record PollEveryStep(SourceLocation Location, TimeSpan Interval) : ScriptStep(Location)
{
    public override string Describe() => $"poll every {Interval.TotalMilliseconds:F0}ms";
}

/// <summary>Waits, doing nothing but keeping the poll loop running.</summary>
public sealed record WaitStep(SourceLocation Location, TimeSpan Duration) : ScriptStep(Location)
{
    public override string Describe() => $"wait {Duration.TotalMilliseconds:F0}ms";
}

/// <summary>
/// Lets the general polls run for the given time, once per machine in a run, so a machine that has
/// just started reports its start-up exceptions before the first case that needs a quiet queue.
/// </summary>
public sealed record WarmUpStep(SourceLocation Location, TimeSpan Duration) : ScriptStep(Location)
{
    public override string Describe() =>
        $"warm up for {Duration.TotalMilliseconds:F0}ms, once per machine in a run";
}

/// <summary>Records a remark in the report. Never affects the verdict.</summary>
public sealed record NoteStep(SourceLocation Location, string Text) : ScriptStep(Location)
{
    public override string Describe() => $"note {Text}";
}

/// <summary>Sends one long poll.</summary>
public sealed record SendLongPollStep(SourceLocation Location, byte Command, byte[] Data)
    : ScriptStep(Location)
{
    /// <summary>
    /// Which machine to send it to, when the step names one rather than the case having moved.
    /// </summary>
    /// <remarks>
    /// Null on a host bound to one machine, which is every case written before rounds existed. On
    /// a round it is either this or a preceding <see cref="UseMachineStep"/>: a command sent to
    /// whichever machine the round had reached would be answered correctly by the wrong machine.
    /// </remarks>
    public SasAddress? To { get; init; }

    /// <summary>
    /// How the data was written, when it was written as named fields rather than as bytes.
    /// </summary>
    /// <remarks>
    /// The bytes are the same either way — the names are turned into them while the script is
    /// read, so a misspelt field stops the parse rather than a run. This is kept only so the
    /// report shows the line the way it was written; a step whose log reads
    /// <c>data 00000000200000...</c> is one nobody can check against the script.
    /// </remarks>
    public string? NamedData { get; init; }

    /// <summary>
    /// What the machine has to say about itself for this to be sent, when the step names it.
    /// </summary>
    /// <remarks>
    /// Asked when the case starts, on its first link, as a requirement is; no answer fails the step.
    /// </remarks>
    public Requirement? When { get; init; }

    public override string Describe()
    {
        string what = NamedData is not null
            ? $"send lp {Command:X2} with {NamedData}"
            : Data.Length == 0
                ? $"send lp {Command:X2}"
                : $"send lp {Command:X2} data {Convert.ToHexString(Data)}";

        if (To is { } machine)
            what = $"{what} to {machine}";

        return When is { } condition ? $"{what} when {condition}" : what;
    }
}

/// <summary>
/// Expects a long poll to be met with silence rather than with a reply or a refusal.
/// </summary>
/// <remarks>
/// Silence is a fact about the machine and one this build had no way to assert. It is how the
/// gaming machine library says four different things: the command belongs to a category this
/// channel was not given, the machine is not performing the validation the command is about, the
/// message was too short for the receiver to frame, or the machine has no handler. A refusal says
/// something else — the command was understood and declined — so this step distinguishes the two
/// and reports a refusal as a failure rather than as silence.
/// </remarks>
public sealed record ExpectNoReplyStep(SourceLocation Location, byte Command, byte[] Data)
    : ScriptStep(Location)
{
    public override string Describe() => $"expect no reply to lp {Command:X2}";
}

/// <summary>
/// Stops the round and the synchronisation poll, so the line carries exactly the messages the
/// case names.
/// </summary>
/// <remarks>
/// What is on the line between two messages is the protocol, not scaffolding: a machine learns the
/// host heard its last reply from the next poll it sees, and the synchronisation poll this host
/// sends ahead of every message is a global broadcast, which section 3.19 makes an acknowledgement
/// in its own right. Every implied-acknowledgement and implied-NACK case needs this mode.
/// </remarks>
public sealed record PollInSequenceStep(SourceLocation Location) : ScriptStep(Location)
{
    public override string Describe() => "poll in sequence";
}

/// <summary>What a general poll placed by a case is expected to draw.</summary>
public enum DrawExpectation
{
    /// <summary>Nothing is asserted; the poll is placed for what it does to the machine.</summary>
    Unasserted,

    /// <summary>One named exception.</summary>
    Exception,

    /// <summary>The machine answering that it has nothing to report.</summary>
    NoActivity,

    /// <summary>No answer at all, which is what a poll to another machine must draw.</summary>
    Silence,
}

/// <summary>Sends one general poll by name, and says what it drew.</summary>
public sealed record SendGeneralPollStep(SourceLocation Location) : ScriptStep(Location)
{
    /// <summary>Which machine to poll, when the step names one rather than the case having moved.</summary>
    public SasAddress? To { get; init; }

    /// <summary>What the poll is expected to draw, when the case says.</summary>
    public DrawExpectation Draws { get; init; } = DrawExpectation.Unasserted;

    /// <summary>The exception code expected, when one is.</summary>
    public byte Code { get; init; }

    /// <summary>The name of that exception, for the report.</summary>
    public string? CodeName { get; init; }

    public override string Describe()
    {
        string target = To is { } address ? $" to {address}" : string.Empty;
        string draws = Draws switch
        {
            DrawExpectation.Exception => $" draws {CodeName}",
            DrawExpectation.NoActivity => " draws no activity",
            DrawExpectation.Silence => " draws silence",
            _ => string.Empty,
        };

        return $"send gp{target}{draws}";
    }
}

/// <summary>Sends the broadcast to address 00, which acknowledges every machine on the line.</summary>
public sealed record SendSyncPollStep(SourceLocation Location) : ScriptStep(Location)
{
    public override string Describe() => "send sync";
}

/// <summary>Expects an exception to arrive, or expects it not to.</summary>
public sealed record ExpectExceptionStep(
    SourceLocation Location,
    byte Code,
    string Name,
    TimeSpan Within,
    bool Negated) : ScriptStep(Location)
{
    public override string Describe() => Negated
        ? $"expect no exception {Name} within {Within.TotalMilliseconds:F0}ms"
        : $"expect exception {Name} within {Within.TotalMilliseconds:F0}ms";
}

/// <summary>Expects a meter to have moved, or not moved.</summary>
/// <remarks>
/// A relative comparison is measured against the value read the last time a baseline was taken:
/// at the start of the test, and again immediately before each stimulus step. That is what makes
/// <c>+= 2000</c> mean what a reader expects it to mean after a note has been inserted.
/// </remarks>
public sealed record ExpectMeterStep(
    SourceLocation Location,
    string Meter,
    MeterComparison Comparison,
    long Value,
    TimeSpan Within) : ScriptStep(Location)
{
    public override string Describe()
    {
        string operation = Comparison switch
        {
            MeterComparison.IncreasedBy => $"+= {Value}",
            MeterComparison.IncreasedByAtLeast => $"+>= {Value}",
            MeterComparison.Unchanged => "unchanged",
            MeterComparison.Equals => $"== {Value}",
            MeterComparison.AtLeast => $">= {Value}",
            MeterComparison.AtMost => $"<= {Value}",
            _ => Comparison.ToString(),
        };

        return $"expect meter {Meter} {operation}";
    }
}

/// <summary>Expects a field of a long poll reply to hold a particular value.</summary>
public sealed record ExpectFieldStep(
    SourceLocation Location,
    byte Command,
    string Field,
    SasComparison Comparison,
    long? ExpectedNumber,
    string? ExpectedText) : ScriptStep(Location)
{
    /// <summary>
    /// The field of the ticket the machine last printed that this is compared against, when the
    /// value is not a literal.
    /// </summary>
    /// <remarks>
    /// The only way to check that the host was told about <em>this</em> ticket. A machine numbers
    /// its own tickets, so the number is unknowable to whoever wrote the script; comparing the
    /// reply against a literal can say the number is plausible but never that it is the number
    /// on the paper, which is the whole claim a cashout ticket test is making.
    /// </remarks>
    public string? ExpectedTicketField { get; init; }

    public override string Describe()
    {
        string expected = ExpectedTicketField is not null
            ? $"ticket.{ExpectedTicketField}"
            : ExpectedText ?? ExpectedNumber?.ToString() ?? "?";
        return $"expect field {Field} of lp {Command:X2} {Comparison} {expected}";
    }
}

/// <summary>
/// Expects a field of a real-time event's payload to hold a particular value.
/// </summary>
/// <remarks>
/// Separate from <see cref="ExpectFieldStep"/> because an event is waited for rather than asked
/// for. A reply can be read again; an event happened once, so the step matches one that arrived
/// inside its window and consumes it the way an exception expectation does.
/// </remarks>
public sealed record ExpectEventFieldStep(
    SourceLocation Location,
    byte Code,
    string EventName,
    string Field,
    SasComparison Comparison,
    long? ExpectedNumber,
    string? ExpectedText,
    TimeSpan Within) : ScriptStep(Location)
{
    public override string Describe()
    {
        string expected = ExpectedText ?? ExpectedNumber?.ToString() ?? "?";
        return $"expect field {Field} of event {EventName} {Comparison} {expected}";
    }
}

/// <summary>
/// Leaves every exception reported so far out of the assertions that follow.
/// </summary>
/// <remarks>
/// A machine reports one exception per poll, so a step that causes ten leaves nine of them
/// arriving over the following seconds, still unclaimed when the next assertion runs. This says
/// that everything up to here belongs to the setting-up and is not what the test is about. The
/// wire log keeps all of them: what changes is only what an assertion may match.
/// </remarks>
public sealed record ForgetExceptionsStep(SourceLocation Location, TimeSpan Within)
    : ScriptStep(Location)
{
    public override string Describe() =>
        $"forget exceptions, waiting up to {Within.TotalMilliseconds:F0}ms for quiet";
}

/// <summary>Expects the link to be in a given state.</summary>
public sealed record ExpectLinkStep(SourceLocation Location, LinkState Expected, TimeSpan Within)
    : ScriptStep(Location)
{
    public override string Describe() =>
        $"expect link {Expected.ToString().ToLowerInvariant()} within {Within.TotalMilliseconds:F0}ms";
}

/// <summary>A step that needs something to drive the gaming machine.</summary>
public abstract record StimulusStep(SourceLocation Location) : ScriptStep(Location)
{
    /// <summary>The capability this step needs, so an unmet one can be named in a skip.</summary>
    public abstract string RequiredCapability { get; }
}

/// <summary>Inserts a note.</summary>
public sealed record InsertBillStep(SourceLocation Location, int Cents) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.bill";

    public override string Describe() => $"bill insert {Cents}";
}

/// <summary>Plays games.</summary>
public sealed record PlayGamesStep(
    SourceLocation Location,
    int Games,
    long BetCredits,
    long WinCredits) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.play";

    public override string Describe() => WinCredits > 0
        ? $"play games={Games} bet={BetCredits} win={WinCredits}"
        : $"play games={Games} bet={BetCredits}";
}

/// <summary>Presses a button.</summary>
public sealed record PressButtonStep(SourceLocation Location, string Button) : StimulusStep(Location)
{
    public override string RequiredCapability => $"stimulus.button.{Button}";

    public override string Describe() => $"press {Button}";
}

/// <summary>Opens or closes a door.</summary>
public sealed record DoorStep(SourceLocation Location, string Door, bool Open) : StimulusStep(Location)
{
    public override string RequiredCapability => $"stimulus.door.{Door}";

    public override string Describe() => $"door {Door} {(Open ? "open" : "close")}";
}

/// <summary>Feeds a ticket into the machine's acceptor.</summary>
/// <remarks>
/// The step carries the ticket itself, not the file it may have come from, because the two ways of
/// writing it have to end at the same place: a ticket named by file is read while the script is
/// parsed, so a path that is wrong or a file that is not a ticket stops the parse rather than a
/// run. What the machine is given is a ticket either way.
/// </remarks>
public sealed record InsertTicketStep(SourceLocation Location, Tickets.Ticket Ticket, string? File)
    : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.ticket";

    public override string Describe() => File is not null
        ? $"ticket insert {System.IO.Path.GetFileName(File)}"
        : $"ticket insert amount={Ticket.AmountCents} validation={Ticket.ValidationNumber}";
}

/// <summary>Brings the machine to a known state: every door shut, every peripheral connected.</summary>
/// <remarks>
/// A step rather than something the runner does quietly, because settling a machine puts real
/// door exceptions on the wire. A machine that believes a door is open at power-up — and one
/// fresh from a cold start usually does — can only be told otherwise by opening and shutting it,
/// so the exceptions are unavoidable. What is avoidable is a test being surprised by them.
/// </remarks>
public sealed record SettleStep(SourceLocation Location) : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.settle";

    public override string Describe() => "settle to a known state";
}

/// <summary>
/// Waits for the machine's printer to file a ticket in the wallet, and remembers it so the
/// assertions that follow have something to be about.
/// </summary>
/// <remarks>
/// This is an observation of the machine rather than of the wire: a ticket is a thing the printer
/// produced, and the host learns of it by reading the directory all three programs share. What the
/// printer was told is all the ticket carries — a pool, a validation system, an asset number and a
/// location are never handed to it, so a test that wants those checks them against its own record
/// of what it sent, not against the paper.
/// </remarks>
public sealed record ExpectTicketStep(SourceLocation Location, TimeSpan Within) : ScriptStep(Location)
{
    public override string Describe() => $"expect a ticket printed within {Within.TotalSeconds:0.##}s";
}

/// <summary>Asserts one field of the ticket the machine last printed.</summary>
public sealed record ExpectTicketFieldStep(
    SourceLocation Location,
    string Field,
    SasComparison Comparison,
    long? ExpectedNumber,
    string? ExpectedText) : ScriptStep(Location)
{
    public override string Describe()
    {
        string expected = ExpectedText ?? ExpectedNumber?.ToString() ?? "?";
        return $"expect ticket {Field} {Comparison} {expected}";
    }
}

/// <summary>Changes the accounting denomination the machine is counting in, during a run.</summary>
/// <remarks>
/// <para>
/// A stimulus like any other, which is what keeps it honest: section 16 makes the unit a property
/// a memory clear establishes, so no real machine can honour this. On one that cannot be
/// reconfigured the step refuses with a reason and the case skips, exactly as a door nobody has
/// wired does.
/// </para>
/// <para>
/// Values already counted are left in the unit they were counted in. Rescaling them would either
/// invent money or truncate it away, and a machine cannot be in two units at once — so a case that
/// wants a clean measurement declares its denomination instead of changing one mid-run.
/// </para>
/// </remarks>
public sealed record SetDenominationStep(SourceLocation Location, byte Code, int Cents)
    : StimulusStep(Location)
{
    public override string RequiredCapability => "stimulus.accounting_denomination";

    public override string Describe() =>
        $"set accounting denomination {MachineDeclaration.Describe(Cents)}";
}
