using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Qpm;

/// <summary>The real demo's menus, faults, plays, restarts and window keys (D63).</summary>
public sealed partial class QpmStimulusBackend
{
    private const byte GameStarted = 0x7E;
    private const byte GameEnded = 0x7F;
    private const ushort KeyF10 = 0x79;

    /// <summary>How long a press of play may take to start a game.</summary>
    private static readonly TimeSpan GameStartWithin = TimeSpan.FromSeconds(10);

    /// <summary>How long a game may take, from play to its end, win display included.</summary>
    private static readonly TimeSpan GameEndWithin = TimeSpan.FromSeconds(90);

    /// <summary>How long the demo has to write its line for a key it was posted.</summary>
    private static readonly TimeSpan KeyAnsweredWithin = TimeSpan.FromSeconds(5);

    /// <summary>Where the exceptions stood when this case's own game was started, or null when none was.</summary>
    private long? _gameStartMark;

    /// <summary>Carries a step only the demo's own levers can do, its refusals already checked; false when it is not one of those.</summary>
    private bool CarryOnTheDemo(StimulusStep step, IHostView? host)
    {
        switch (step)
        {
            // The attendant's key alone, as the step means: the display-meters screens need no door open.
            case MenuStep menu:
                Machine.SetButton(AuditKey(Machine.Profile)!, menu.Enter);
                return true;

            case PeripheralFaultStep fault:
                Machine.SetNoteAcceptorJammed(fault.Fault != PeripheralFaultStep.Cleared);
                return true;

            case PlayGamesStep play:
                Play(play, RequireHost(host, play));
                return true;

            case ProgressiveWinStep win:
                WinProgressive(win);
                return true;

            case StartGameStep start:
                StartGame(start, RequireHost(host, start));
                return true;

            case EndGameStep end:
                EndGame(RequireHost(host, end));
                return true;

            case SelectGameStep select:
                SelectGame(select, RequireHost(host, select));
                return true;

            case PowerCycleStep:
                PowerCycle();
                return true;

            case AftRegisterStep:
                Register(host);
                return true;

            default:
                return false;
        }
    }

    /// <summary>What the demo's own levers add to the offer.</summary>
    private static void OfferOnTheDemo(
        Dictionary<string, string?> values, EgmHardwareProfile profile, DemoProcess? demo, long? betCredits)
    {
        if (AuditKey(profile) is not null)
            values[MenuStep.Capability] = "yes";

        // The jam bit is part of the shared region, so it exists whenever the region does.
        values["stimulus.note_fault"] = "yes";

        // Restarting the executable and posting a key to its window both need the run to say which one it is.
        if (demo is not null)
        {
            values["machine.power_cycle"] = "yes";
            values["stimulus.aft_registration"] = "yes";
            values["machine.aft_operator_registration"] = "yes";
        }

        // A run that says what a bet multiplier wagers has agreed to the demo being played on.
        bool canPlay = profile.TryFindButton("play", out _) && BetButtons(profile).Any();
        if (betCredits is > 0 && canPlay)
        {
            values["stimulus.play"] = "yes";
            values["stimulus.game_in_progress"] = "yes";
            if (profile.TryFindButton("game_menu", out _))
                values["stimulus.select_game"] = "yes";
        }
    }

    private void Play(PlayGamesStep play, IHostView view)
    {
        EgmButton? bet = BetFor(play.BetCredits);
        EgmButton start = Button("play");

        for (int game = 1; game <= play.Games; game++)
        {
            if (bet is not null)
                Machine.PressButton(bet);

            long mark = view.Mark();
            if (play.WinStated)
                DrawScripted(DemoOutcomes.Lose, start, $"{play.Describe()}, game {game}");
            else
                Machine.PressButton(start);

            if (view.ExceptionAfter(GameEnded, mark, GameEndWithin) is null)
            {
                throw new StimulusUnavailableException(
                    $"game {game} of {play.Games}: no game ended within {GameEndWithin.TotalSeconds:F0}s of pressing "
                    + "play, so the machine has no credit for the bet or a condition stops play");
            }
        }
    }

    private void StartGame(StartGameStep start, IHostView host)
    {
        if (BetFor(start.BetCredits) is { } bet)
            Machine.PressButton(bet);

        long mark = host.Mark();
        Machine.PressButton(Button("play"));
        if (host.ExceptionAfter(GameStarted, mark, GameStartWithin) is null)
        {
            throw new StimulusUnavailableException(
                $"no game started within {GameStartWithin.TotalSeconds:F0}s of pressing play, so the machine has no "
                + "credit for the bet or a condition stops play");
        }

        _gameStartMark = mark;
    }

    private void EndGame(IHostView host)
    {
        long mark = _gameStartMark ?? throw new StimulusUnavailableException(
            "this case started no game here, so there is none to end");
        _gameStartMark = null;

        // The demo ends a spin by itself; one already over means the steps meant to run during it did not.
        if (host.ExceptionAfter(GameEnded, mark, TimeSpan.Zero) is not null)
        {
            throw new StimulusUnavailableException(
                "the game had already ended before this step: the machine's spin is shorter than the steps held inside "
                + "it, so they did not run during a game");
        }

        if (host.ExceptionAfter(GameEnded, mark, GameEndWithin) is null)
        {
            throw new StimulusUnavailableException(
                $"the game started by this case did not end within {GameEndWithin.TotalSeconds:F0}s");
        }
    }

    private void SelectGame(SelectGameStep select, IHostView host)
    {
        int first = FirstEnabledGame(host);
        if (select.Game != first)
        {
            throw new StimulusUnavailableException(
                $"only game {first}, the first enabled one, can be chosen here: the game menu goes to it, and any other "
                + "choice is made by touch");
        }

        EgmButton menu = Button("game_menu");
        int selected = SelectedGame(host);
        for (int press = 0; press < 2 && selected != first; press++)
        {
            Machine.PressButton(menu);
            selected = SelectedGameWithin(host, first, TimeSpan.FromSeconds(3));
        }

        if (selected != first)
            throw new StimulusUnavailableException($"after the game menu the machine still reports game {selected}, not {first}");
    }

    private void PowerCycle()
    {
        DemoProcess process = RequireDemo("power cycle");
        process.Restart(stopWithin: TimeSpan.FromSeconds(15), stayUpFor: TimeSpan.FromSeconds(5));

        // The first change may be the new process resetting the counter, so it must go on advancing after it.
        if (!Machine.WaitForHeartbeat(1, TimeSpan.FromSeconds(90)) || !Machine.WaitForHeartbeat(4, TimeSpan.FromSeconds(10)))
        {
            throw new StimulusUnavailableException(
                "the machine was started again but its peripheral heartbeat did not resume within 90s");
        }
    }

    // F10 asks to register or acknowledges, as the status decides; its log line is the proof the key arrived.
    private void Register(IHostView? host)
    {
        DemoProcess process = RequireDemo("AFT registration");
        int before = process.LogLineCount();
        process.PostKey(KeyF10);

        string line = process.WaitForLogLine(before, "AFT registration:", KeyAnsweredWithin)?.Trim()
            ?? throw new StimulusUnavailableException(
                $"F10 was posted to the machine's window but its log ({process.LogPath}) shows no registration action "
                + $"within {KeyAnsweredWithin.TotalSeconds:F0}s, so the key did not reach the game");

        if (line.Contains("refused", StringComparison.Ordinal))
            throw new StimulusUnavailableException($"the machine refused the register action: {line}");

        if (host is null || host.Unavailable is not null || StatusIn(line) is not { } logged)
            return;

        LongPollResult read = host.Ask(0x73, [0x01, 0xFF]);
        byte? status = read.Succeeded && read.Payload.Length >= 2 ? read.Payload[1] : null;
        if (status != logged)
        {
            throw new StimulusUnavailableException(
                $"the machine logged registration status ${logged:X2} but long poll 73 reads "
                + (status is { } s ? $"${s:X2}" : read.ToString()));
        }
    }

    /// <summary>The status the demo writes after an accepted action, as in "status now 0x40".</summary>
    public static byte? StatusIn(string line)
    {
        int at = line.LastIndexOf("0x", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && line.Length >= at + 4
               && byte.TryParse(line.AsSpan(at + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte value)
            ? value
            : null;
    }

    private static int FirstEnabledGame(IHostView host)
    {
        LongPollResult read = host.Ask(0x56, []);
        ReadOnlySpan<byte> payload = read.Payload;
        if (!read.Succeeded || payload.Length < 4 || payload[1] == 0)
            throw new StimulusUnavailableException($"the machine's enabled games could not be read: {read}");

        return Bcd(payload.Slice(2, 2));
    }

    // The game takes a moment to change after the press, and pressing again too soon would leave it.
    private static int SelectedGameWithin(IHostView host, int wanted, TimeSpan within)
    {
        long deadline = Environment.TickCount64 + (long)within.TotalMilliseconds;
        int selected = SelectedGame(host);
        while (selected != wanted && Environment.TickCount64 < deadline)
            selected = SelectedGame(host);
        return selected;
    }

    private static int SelectedGame(IHostView host)
    {
        LongPollResult read = host.Ask(0x55, []);
        if (!read.Succeeded || read.Payload.Length < 2)
            throw new StimulusUnavailableException($"the machine's selected game could not be read: {read}");

        return Bcd(read.Payload[..2]);
    }

    private static int Bcd(ReadOnlySpan<byte> digits)
    {
        int value = 0;
        foreach (byte pair in digits)
            value = value * 100 + (pair >> 4) * 10 + (pair & 0x0F);
        return value;
    }

    /// <summary>The bet button that wagers this many credits, or null for a bet the script left unsaid.</summary>
    private EgmButton? BetFor(long credits) =>
        WhyNoBet(credits) is { } why
            ? throw new StimulusUnavailableException(why)
            : credits == 0 ? null : BetButtons(Machine.Profile).First(b => b.Data == credits / betCredits!.Value);

    /// <summary>Why a bet of this many credits cannot be placed, or null when a bet button wagers it or none was asked for.</summary>
    private string? WhyNoBet(long credits)
    {
        if (credits == 0)
            return null;

        if (betCredits is not { } step || step <= 0)
            return "how many credits a bet multiplier wagers on this machine was not declared: run with --egm-bet-credits <n>";

        return credits % step == 0 && BetButtons(Machine.Profile).Any(b => b.Data == credits / step)
            ? null
            : $"a bet of {credits} credits cannot be placed: the machine wagers {step} credits per multiplier, and its "
              + "bet buttons offer " + string.Join(", ", BetButtons(Machine.Profile).Select(b => $"x{b.Data}"));
    }

    private EgmButton Button(string name) =>
        Machine.Profile.TryFindButton(name, out EgmButton button)
            ? button
            : throw new StimulusUnavailableException(WhyNoButton(name));

    private DemoProcess RequireDemo(string what) =>
        demo ?? throw new StimulusUnavailableException(
            $"{what} on this machine acts on its executable, which a run names so that no other process is touched: "
            + "run with --egm-exe <path>");

    private static IHostView RequireHost(IHostView? host, StimulusStep step) =>
        host is null
            ? throw new StimulusUnavailableException($"'{step.Describe()}' is confirmed on the wire, and no host is attached")
            : host.Unavailable is { } why
                ? throw new StimulusUnavailableException($"'{step.Describe()}' cannot be confirmed: {why}")
                : host;

    private static IEnumerable<EgmButton> BetButtons(EgmHardwareProfile profile) =>
        profile.Buttons.Where(b => b.Kind.Equals("bet", StringComparison.OrdinalIgnoreCase) && b.Data > 0);

    /// <summary>The keyswitch that opens the attendant's audit screens, or null when the machine has none.</summary>
    private static EgmButton? AuditKey(EgmHardwareProfile profile) =>
        profile.Buttons.FirstOrDefault(b =>
            b.Kind.Equals("keyswitch", StringComparison.OrdinalIgnoreCase)
            && b.Label.Contains("audit", StringComparison.OrdinalIgnoreCase));
}
