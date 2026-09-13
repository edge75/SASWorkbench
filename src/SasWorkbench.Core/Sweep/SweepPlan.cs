using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Sweep;

/// <summary>Whether the sweep may send a command to a machine nobody is watching.</summary>
public enum SweepDisposition
{
    /// <summary>The command asks the machine something and leaves it as it was found.</summary>
    Ask,

    /// <summary>
    /// The command changes the machine, so the sweep does not send it. Refused by name and with
    /// a reason, never sent and hoped about.
    /// </summary>
    Withheld,
}

/// <summary>One command's place in the sweep.</summary>
/// <param name="Command">The command byte.</param>
/// <param name="Disposition">Whether the sweep sends it.</param>
/// <param name="Reason">Why, in one sentence, in terms of what the machine does about it.</param>
/// <param name="Request">
/// The request bytes to send, when the command needs particular ones and the ladder would not
/// find them. Null lets the sweep work up from the shortest form.
/// </param>
public sealed record SweepEntry(
    byte Command, SweepDisposition Disposition, string Reason, byte[]? Request = null)
{
    public string Name =>
        SasCatalogue.LongPolls.TryGetValue(Command, out SasLongPollInfo? info)
            ? info.Description
            : "not in the catalogue";
}

/// <summary>
/// Which of the catalogue's commands may be sent to a machine unattended, and which may not.
/// </summary>
/// <remarks>
/// <para>
/// The rule, stated once so it can be checked rather than taken on trust: <b>a command whose
/// handler in the gaming machine library is named for sending something is asked; every other
/// command is withheld and named here with its reason</b>, unless it carries an argument that
/// makes the handler read rather than write, in which case it is asked with that argument and
/// with nothing else. The library's handler names are first-party and describe what the handler
/// does, which is why they can carry a safety judgement that a name invented here could not.
/// </para>
/// <para>
/// The rule is enforced rather than documented: a test walks the whole catalogue and fails if a
/// command whose handler is not a <c>Send</c> is neither in <see cref="Withheld"/> nor named in
/// the read-only table with its argument. So a setter added to the library arrives as a failing
/// test, not as a command the sweep quietly sends to somebody's machine.
/// </para>
/// <para>
/// Where the judgement was uncertain the command is withheld. That costs coverage and cannot cost
/// anything else, and every one of them is listed with what it would have changed.
/// </para>
/// </remarks>
public static class SweepPlan
{
    /// <summary>Commands the sweep will not send, and what each would change.</summary>
    private static readonly Dictionary<byte, string> WithheldCommands = new()
    {
        [0x00] = "not a command: the enumeration carries it as the library's own internal marker",
        [0x01] = "stops play on the machine",
        [0x02] = "starts play on a machine that may have been deliberately stopped",
        [0x03] = "turns the machine's sound off",
        [0x04] = "turns the machine's sound on",
        [0x05] = "silences reel spin sounds",
        [0x06] = "enables the note acceptor, so the machine starts taking money",
        [0x07] = "disables the note acceptor, so the machine stops taking money",
        [0x08] = "sets which notes the machine accepts",
        [0x09] = "enables or disables one of the machine's games",
        [0x0A] = "puts the machine into maintenance mode",
        [0x0B] = "takes the machine out of maintenance mode",
        [0x0C] = "sets the machine's sound volume, and the library dispatches it nowhere",
        [0x0D] = "makes the machine play a sound, and the library dispatches it nowhere",
        [0x0E] = "changes how the machine answers every general poll after it, which would change "
                 + "the meaning of the rest of the sweep",
        [0x21] = "makes the machine compute a signature over its own code and report it later as "
                 + "an exception, which is a state the sweep would walk away from",
        [0x2E] = "stops the game for a period the host chooses",
        [0x4C] = "sets the identifier the machine derives its validation numbers from",
        [0x58] = "gives the machine a validation number for a ticket it is holding",
        [0x71] = "redeems a ticket, which moves money",
        [0x72] = "transfers funds to or from the machine",
        [0x73] = "registers a host against the machine for funds transfer",
        [0x75] = "sets the text printed on a funds transfer receipt",
        [0x76] = "sets the machine's custom ticket text",
        [0x7C] = "sets the machine's extended ticket text",
        [0x7D] = "sets the machine's ticket text",
        [0x7F] = "sets the machine's clock",
        [0x80] = "sets a progressive amount the machine will pay",
        [0x86] = "sets the progressive amounts the machine will pay",
        [0x8A] = "pays a legacy bonus into the machine",
        [0x8B] = "pays a multiplied jackpot into the machine",
        [0x8C] = "puts the machine into or out of tournament mode",
        [0x94] = "clears a handpay the machine is holding, which is money owed to a player",
        [0xA8] = "changes who is allowed to reset a jackpot handpay",
        [0xAA] = "makes the machine play rounds by itself",
        [0xB0] = "carries another command inside it, so what it changes is whatever it is given",
        [0xB6] = "tells the machine its meters have been collected",
        [0xB7] = "sets the machine's asset and floor numbers",
        [0xFF] = "not a command: it is the marker a machine puts on a real-time event reply",
    };

    /// <summary>
    /// Requests the sweep sends verbatim, where working up from the shortest form would be the
    /// wrong thing to do rather than merely slow.
    /// </summary>
    /// <summary>
    /// Commands that change the machine when asked one way and only read it when asked another,
    /// with the argument that reads and the branch in the library that makes it a read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one exception to the rule above, and it is deliberately narrow: an entry here
    /// is a claim that <b>for this argument</b> the handler takes no branch that writes, and the
    /// reason names the branch so somebody can check it against the library rather than trust it.
    /// A command with no such argument stays withheld, however useful its reply would be.
    /// </para>
    /// <para>
    /// Both entries were then measured rather than argued: the machine answered, and it was still
    /// unlocked and still validating the same way afterwards.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<byte, (byte[] Request, string Why)> ReadOnlyWithArgument =
        new()
        {
            // Lock code $FF is LC_INTERROGATE, whose whole branch is `gameLockStatus =
            // aft.gameLockStatus` - a read. $00 asks for a lock and $80 cancels one.
            [0x74] = ([0xFF, 0x00, 0x00, 0x00],
                "lock code $FF is the library's LC_INTERROGATE, which reads the lock status and "
                + "takes no lock"),

            // The handler only touches a status bit where the control mask selects it, so a mask
            // of nothing selects nothing and the reply still carries the machine's own settings.
            [0x7B] = ([0x04, 0x00, 0x00, 0x00, 0x00],
                "the handler changes only the status bits the control mask selects, and this mask "
                + "selects none"),
        };

    private static readonly Dictionary<byte, byte[]> FixedRequests = new()
    {
        // Function code $FF reads the current validation entry and leaves the machine's buffer
        // where it was. Function code zero reads the same entry and advances past it, which would
        // consume a record nobody has seen. The ladder would send zero first.
        [0x4D] = [0xFF],

        // Action $00 interrogates how many components are installed and authenticates nothing.
        // The ladder's shortest form gives this command a length byte of zero, after which the
        // handler reads the check sequence as the action code and refuses it - so without this
        // the command's silence would be about the request and not about the machine.
        [0x6E] = [0x01, 0x00],
    };

    /// <summary>Every command in the catalogue, with what the sweep does about it.</summary>
    public static IReadOnlyList<SweepEntry> All { get; } = Build();

    /// <summary>The commands the sweep sends.</summary>
    public static IEnumerable<SweepEntry> Asked =>
        All.Where(e => e.Disposition == SweepDisposition.Ask);

    /// <summary>The commands the sweep refuses to send, with the reason for each.</summary>
    public static IEnumerable<SweepEntry> Withheld =>
        All.Where(e => e.Disposition == SweepDisposition.Withheld);

    /// <summary>
    /// The one argument that makes this command read rather than write, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Asked for by anything that sends a command it did not choose itself. A recording carries
    /// whatever argument the host that made it used, and for these two commands that may be the
    /// one that locks the machine or changes how it validates — so a replay has to compare rather
    /// than trust the disposition.
    /// </remarks>
    public static byte[]? ReadOnlyArgument(byte command) =>
        ReadOnlyWithArgument.TryGetValue(command, out (byte[] Request, string Why) only)
            ? only.Request
            : null;

    /// <summary>The commands that have such an argument, for the tests that name them.</summary>
    public static IReadOnlyCollection<byte> ReadOnlyWithArgumentCommands =>
        ReadOnlyWithArgument.Keys;

    /// <summary>What the sweep does about one command.</summary>
    public static SweepEntry For(byte command) =>
        All.FirstOrDefault(e => e.Command == command)
        ?? new SweepEntry(command, SweepDisposition.Withheld,
            "not in this build's catalogue, so nothing here knows what it does");

    /// <summary>
    /// True when the library's handler for this command is named for sending something back,
    /// which is the rule that decides everything not named in the withheld table.
    /// </summary>
    public static bool HandlerOnlyAnswers(byte command)
    {
        if (!SasCatalogue.LongPolls.TryGetValue(command, out SasLongPollInfo? info)
            || info.Handler is null)
        {
            return false;
        }

        int underscore = info.Handler.IndexOf('_');
        return underscore >= 0
               && info.Handler.AsSpan(underscore + 1).StartsWith("Send", StringComparison.Ordinal);
    }

    private static List<SweepEntry> Build()
    {
        var entries = new List<SweepEntry>();
        foreach (byte command in SasCatalogue.LongPolls.Keys.Order())
        {
            if (ReadOnlyWithArgument.TryGetValue(command, out (byte[] Request, string Why) only))
            {
                entries.Add(new SweepEntry(command, SweepDisposition.Ask, only.Why, only.Request));
                continue;
            }

            if (WithheldCommands.TryGetValue(command, out string? reason))
            {
                entries.Add(new SweepEntry(command, SweepDisposition.Withheld, reason));
                continue;
            }

            // The rule, applied rather than only documented. A command that is neither named in
            // the table above nor answered by a handler named for sending something is withheld,
            // so a command added to the library arrives as one this sweep will not send until
            // somebody has looked at it. The test that walks the catalogue is what turns that into
            // a failure rather than a silent gap in coverage.
            if (!HandlerOnlyAnswers(command))
            {
                entries.Add(new SweepEntry(command, SweepDisposition.Withheld,
                    "not classified here, and the library's handler is not one that only answers"));
                continue;
            }

            FixedRequests.TryGetValue(command, out byte[]? request);
            entries.Add(new SweepEntry(command, SweepDisposition.Ask,
                $"the library answers it in {SasCatalogue.LongPolls[command].Handler}, which "
                + "sends and changes nothing",
                request));
        }

        return entries;
    }
}
