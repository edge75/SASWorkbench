using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>One progressive win in the library's queue, as long polls $85 and $87 read it.</summary>
internal readonly record struct ProgressiveWin(byte Level, long Cents, bool Sas);

/// <summary>
/// Progressives: the level broadcasts $80 and $86 with their five-second watch and $53, the win
/// queue $84, $85 and $87 read with $56 reissued behind it, $83, and a level won and paid by the machine.
/// </summary>
/// <remarks>
/// <para>
/// The protocol half is the machine library's, check for check (<c>lp80_SingleLevelProgressive</c>,
/// <c>lp83</c> to <c>lp87</c>, <c>sasSetProgressiveLevelWin</c>, <c>internalDequeueProgressiveWins</c>,
/// <c>internalProgressiveBroadcastTimeout</c>). What it hands to the application - which levels a
/// host feeds, what a win is worth, when it is announced and paid, and the lockup while a level is
/// stale - is answered as QSP answers it (<c>SasNetwork_Progressives.hpp</c>), reduced to what this machine has.
/// </para>
/// <para>
/// A win above the jackpot limit is paid by an attendant, as the round's hand pay (FakeEgmGameInProgress.cs).
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    // SAS_PROGRESSIVE_LEVELS (libsas_db.h:80): levels 1 to 32, and the queue holds that many and one more.
    private const int QueueCapacity = 33;

    // SAS_LP87_MAX_RECORDS (libsas_db.h:82).
    private const int Lp87MaxRecords = 32;

    // A level the machine runs itself stands at its own amount; this machine starts every one at $10.00.
    private const long NonSasLevelCents = 1000;

    // The broadcast windows and the win-pending timer are all five seconds (internalSetTimer(..., 500)).
    private static readonly TimeSpan ProgressiveWindow = TimeSpan.FromSeconds(5);

    private enum BroadcastsState
    {
        None,
        Counting,
        TimedOut,
    }

    private readonly long[] _levelAmounts = new long[QueueCapacity];
    private readonly long[] _levelDeadlines = new long[QueueCapacity];
    private readonly List<ProgressiveWin> _progressiveWins = [];
    private readonly Dictionary<SasChannel, (byte[] Frame, bool All)> _unacknowledgedProgressiveReads = [];
    private (byte Level, long Cents) _lastProgressiveWin;
    private BroadcastsState _broadcastsState;
    private long _masterBroadcastDeadline;
    private long _winPendingDeadline;
    private int _winPendingCounter;
    private uint? _staleLevels;

    private byte ProgressiveGroup => _options.ProgressiveGroup;

    /// <summary>The levels a host feeds: none on a machine in no group (QSP's linked levels need a group to be fed).</summary>
    private uint SasLevels => _options.ProgressiveGroup == 0 ? 0 : _options.SasProgressiveLevels;

    private bool IsSasLevel(int level) => level is >= 1 and <= 32 && (SasLevels & (1u << (level - 1))) != 0;

    private bool IsNonSasLevel(int level) =>
        level is >= 1 and <= 32 && (_options.NonSasProgressiveLevels & (1u << (level - 1))) != 0;

    /// <summary>Levels still waiting for a broadcast; every linked level starts stale (QSP Qsp::ProgressiveLevelsAwaitingBroadcast).</summary>
    private uint StaleLevelsLocked() => _staleLevels ??= SasLevels;

    /// <summary>The platform locks the machine up while a linked level waits for its broadcast (QSP UpdateProgressiveTimeoutCondition).</summary>
    private bool ProgressiveWaitLocked() => StaleLevelsLocked() != 0;

    /// <summary>The group this channel is told: zero where it does not own the progressive category (libsas_utils.cpp:1276).</summary>
    private byte GroupFor(SasChannel channel) =>
        Channels.OwnerOf(SasCategory.Progressive) == channel ? ProgressiveGroup : (byte)0;

    /// <summary>Answers a progressive poll, and false for any other command; reached after the category gate.</summary>
    private bool AnswerProgressive(MachineLink link, byte command, ReadOnlySpan<byte> frame)
    {
        switch (command)
        {
            case (byte)SasLongPollCode.LP_RECEIVEPROGRESSIVEAMOUNT:
                ReceiveSingleLevel(link, frame);
                return true;
            case (byte)SasLongPollCode.LP_RECEIVEPROGRESSIVELEVELS:
                ReceiveMultipleLevels(link, frame);
                return true;
            case (byte)SasLongPollCode.LP_SENDPROGRESSIVEWIN:
                if (frame[0] == link.Address.Value)
                    SendProgressiveWin(link);
                return true;
            case (byte)SasLongPollCode.LP_SENDSASPROGRESSIVEWIN:
                if (frame[0] == link.Address.Value)
                    SendSasProgressiveWin(link, frame);
                return true;
            case (byte)SasLongPollCode.LP_SENDMULTIPLESASPROGWIN:
                if (frame[0] == link.Address.Value)
                    SendMultipleSasProgressiveWins(link, frame);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Long poll $80, in lp80_SingleLevelProgressive's order: level, group, amount, acknowledgement, timers.</summary>
    private void ReceiveSingleLevel(MachineLink link, ReadOnlySpan<byte> frame)
    {
        bool own = frame[0] == link.Address.Value;

        // Group, level, five BCD bytes and the check sequence; the receiver frames nothing shorter.
        if (frame.Length < 11)
        {
            Ignore(link, frame[1]);
            return;
        }

        byte group = frame[2];
        byte level = frame[3];

        // Levels 1 to 32 only (lp.cpp:7878), then the group must match, group 00 included (lp.cpp:7903).
        if (level == 0 || level >= QueueCapacity || group != ProgressiveGroup)
        {
            if (own)
                Reject(link);
            return;
        }

        long cents = BcdValue(frame.Slice(4, 5));
        lock (_gate)
        {
            _levelAmounts[level] = cents;
            _broadcastsState = BroadcastsState.Counting;
        }

        if (own)
            Send(link, [SasSymbol.Data8(link.Address.Value)]);

        lock (_gate)
        {
            // Only a machine in a SAS group watches the broadcasts, and only its SAS levels (lp.cpp:7945, :7954).
            if (ProgressiveGroup != 0)
            {
                _masterBroadcastDeadline = Deadline(ProgressiveWindow);
                if (IsSasLevel(level))
                    _levelDeadlines[level] = Deadline(ProgressiveWindow);
            }

            FreshenLocked(level);
        }
    }

    /// <summary>Long poll $86, in lp86_MultipleLevelProgressive's order.</summary>
    private void ReceiveMultipleLevels(MachineLink link, ReadOnlySpan<byte> frame)
    {
        bool own = frame[0] == link.Address.Value;

        // The length byte frames the message; one that does not match what arrived was never received.
        if (frame.Length < 5 || frame.Length != 3 + frame[2] + 2)
        {
            Ignore(link, frame[1]);
            return;
        }

        int length = frame[2];
        byte group = frame[3];

        // At most 32 levels of six bytes and the group (lp.cpp:8369).
        if (length > 0xC1)
        {
            if (own)
                Reject(link);
            return;
        }

        // A group mismatch is silent even at the machine's own address: the library's NACK is commented out (lp.cpp:8395, REET-13).
        if (group != ProgressiveGroup)
            return;

        // The library reads six bytes a level up to the length; a length that ends inside an entry is refused here rather than read past the message.
        if ((length - 1) % 6 != 0)
        {
            if (own)
                Reject(link);
            return;
        }

        for (int i = 0; i < length - 1; i += 6)
        {
            byte level = frame[4 + i];

            // A bad level is refused after the levels before it were taken (lp.cpp:8411).
            if (level == 0 || level >= QueueCapacity)
            {
                if (own)
                    Reject(link);
                return;
            }

            long cents = BcdValue(frame.Slice(5 + i, 5));
            lock (_gate)
            {
                _levelAmounts[level] = cents;
                if (ProgressiveGroup != 0 && IsSasLevel(level))
                    _levelDeadlines[level] = Deadline(ProgressiveWindow);
                FreshenLocked(level);
            }
        }

        lock (_gate)
            _broadcastsState = BroadcastsState.Counting;

        if (own)
            Send(link, [SasSymbol.Data8(link.Address.Value)]);

        if (ProgressiveGroup != 0)
        {
            lock (_gate)
                _masterBroadcastDeadline = Deadline(ProgressiveWindow);
        }
    }

    /// <summary>A linked level broadcast in its own group is fresh again (QSP AsyncLpProgressiveMeters, SasNetwork_Progressives.hpp:487).</summary>
    private void FreshenLocked(byte level)
    {
        if (IsSasLevel(level))
            _staleLevels = StaleLevelsLocked() & ~(1u << (level - 1));
    }

    /// <summary>Long poll $84: the most recent win, from its own slot, with the group its level type gives (lp.cpp:8120).</summary>
    private void SendProgressiveWin(MachineLink link)
    {
        (byte Level, long Cents) last;
        lock (_gate)
            last = _lastProgressiveWin;

        byte group = last.Level == 0 ? (byte)0 : IsNonSasLevel(last.Level) ? (byte)0 : GroupFor(link.Channel);
        SendValues(link, 0x84, new Dictionary<string, object>
        {
            ["progressive_group"] = group,
            ["level"] = last.Level,
            ["amount"] = last.Level == 0 ? 0L : last.Cents,
        });
    }

    /// <summary>Long poll $85: the oldest SAS win, or zeros, dequeued once the reply is acknowledged (lp.cpp:8224, :8251).</summary>
    private void SendSasProgressiveWin(MachineLink link, ReadOnlySpan<byte> frame)
    {
        ProgressiveWin? oldest;
        lock (_gate)
            oldest = _progressiveWins.FirstOrDefault(win => win.Sas) is { Sas: true } found ? found : null;

        byte[] request = frame.ToArray();
        SendValues(link, 0x85, new Dictionary<string, object>
        {
            ["progressive_group"] = oldest is null ? (byte)0 : ProgressiveGroup,
            ["level"] = oldest?.Level ?? 0,
            ["amount"] = oldest?.Cents ?? 0L,
        });

        if (ProgressiveGroup != 0)
        {
            lock (_gate)
                _unacknowledgedProgressiveReads[link.Channel] = (request, false);
        }
    }

    /// <summary>Long poll $87: every SAS win at once, up to 32, or an empty answer on a machine in no group (lp.cpp:8534).</summary>
    private void SendMultipleSasProgressiveWins(MachineLink link, ReadOnlySpan<byte> frame)
    {
        byte group = GroupFor(link.Channel);
        List<ProgressiveWin> wins;
        lock (_gate)
            wins = group == 0 ? [] : [.. _progressiveWins.Where(win => win.Sas).Take(Lp87MaxRecords)];

        var values = new Dictionary<string, object>
        {
            ["length"] = 2 + (6 * wins.Count),
            ["progressive_group"] = group,
            ["count"] = wins.Count,
            ["levels.count"] = wins.Count,
        };
        for (int i = 0; i < wins.Count; i++)
        {
            values[$"levels[{i}].level"] = wins[i].Level;
            values[$"levels[{i}].amount"] = wins[i].Cents;
        }

        byte[] request = frame.ToArray();
        SendValues(link, 0x87, values);

        if (group != 0)
        {
            lock (_gate)
                _unacknowledgedProgressiveReads[link.Channel] = (request, true);
        }
    }

    /// <summary>What long poll $83 answers: 001D and 0020 added together, in credits (lp.cpp:8032 to :8046).</summary>
    private Dictionary<string, object>? CumulativeProgressiveWins(SasMessageLayout layout, ReadOnlySpan<byte> request)
    {
        // A game past the last is silence (lp.cpp:8018).
        if (GameNumber(layout, request) is not { } game)
            return NoAnswer;

        return new Dictionary<string, object>
        {
            ["game_number"] = game,
            // Each read for the game asked about, the platform's per-game block for a game (lp.cpp:8033, :8041).
            ["cumulative_progressive_win"] = ReadGameMeter(game, 0, (ushort)SasMeterCode.TotalMachinePaidProgressiveWin_Crd)
                                             + ReadGameMeter(game, 0, (ushort)SasMeterCode.TotalAttendantPaidProgressiveWin_Crd),
        };
    }

    /// <summary>Encodes a reply from its fields and sends it.</summary>
    private void SendValues(MachineLink link, byte command, Dictionary<string, object> values)
    {
        int length = SasMessageEncoder.WriteResponse(SasLayouts.For(command)!, link.Address, values, link.Frame);
        for (int i = 0; i < length; i++)
            link.Outbound[i] = SasSymbol.Data8(link.Frame[i]);
        Send(link, link.Outbound.AsSpan(0, length));
    }

    /// <summary>
    /// Any message other than the same $85 or $87 again acknowledges the reading and runs the dequeue
    /// the library queued behind it (libsas_pending.cpp:274).
    /// </summary>
    private void NoteProgressiveAcknowledgement(MachineLink link, ReadOnlySpan<byte> frame)
    {
        lock (_gate)
        {
            if (!_unacknowledgedProgressiveReads.TryGetValue(link.Channel, out (byte[] Frame, bool All) sent)
                || frame.SequenceEqual(sent.Frame))
            {
                return;
            }

            _unacknowledgedProgressiveReads.Remove(link.Channel);
            DequeueProgressiveWinsLocked(sent.All);
        }
    }

    /// <summary>A loop break is an implied NACK, so the reading it cut off dequeues nothing.</summary>
    private void ForgetProgressiveReading(MachineLink link)
    {
        lock (_gate)
            _unacknowledgedProgressiveReads.Remove(link.Channel);
    }

    /// <summary>internalDequeueProgressiveWins: the oldest SAS win for $85, up to 32 for $87; $56 again for any left.</summary>
    private void DequeueProgressiveWinsLocked(bool all)
    {
        int index = _progressiveWins.FindIndex(win => win.Sas);
        if (index < 0)
            return;

        int removed = 0;
        while (index >= 0 && removed < (all ? Lp87MaxRecords : 1))
        {
            _progressiveWins.RemoveAt(index);
            removed++;
            index = _progressiveWins.FindIndex(win => win.Sas);
        }

        if (index >= 0)
        {
            RaiseException(SasExceptionCode.SASProgressiveLevelHit);
            _winPendingDeadline = Deadline(ProgressiveWindow);
        }
    }

    /// <summary>
    /// Plays one game that wins a level, paid by the machine to its credit meter or by hand above the jackpot limit, the way QSP reports it:
    /// a SAS level is announced at the win and its payment reported at the award, a level the machine
    /// runs itself is reported once, at the award (SasNetwork_Progressives.hpp:104, :425).
    /// </summary>
    /// <exception cref="InvalidOperationException">A level is not configured, waits for its broadcast, or stands at nothing.</exception>
    public void WinProgressive(IReadOnlyList<int> levels, long betCredits, long winCredits = 0)
    {
        var wins = new List<(byte Level, long Cents, bool Sas)>();
        lock (_gate)
        {
            // A linked level waiting for its broadcast holds the machine in a lockup (QSP UpdateProgressiveTimeoutCondition).
            if (ProgressiveWaitLocked())
            {
                throw new InvalidOperationException(
                    "the machine is locked up waiting for the host to broadcast level(s) "
                    + MachineProgressiveLevels(StaleLevelsLocked()) + "; keep them broadcast with 'host broadcast'");
            }

            foreach (int level in levels)
            {
                bool sas = IsSasLevel(level);
                if (!sas && !IsNonSasLevel(level))
                    throw new InvalidOperationException($"level {level} is not one this machine's game is configured with");

                // QSP announces only a real amount (ProgressiveLevelHitRules.h:62).
                long cents = sas ? _levelAmounts[level] : NonSasLevelCents;
                if (cents <= 0)
                    throw new InvalidOperationException($"level {level} stands at nothing, so there is nothing to win");

                wins.Add(((byte)level, cents, sas));
            }
        }

        PlayGames(1, betCredits, winCredits, () => wins.Sum(win => AwardProgressive(win.Level, win.Cents, win.Sas)));
    }

    /// <summary>The win as the round makes it: reported, metered, and added to what the game end reports.</summary>
    private long AwardProgressive(byte level, long cents, bool sas)
    {
        if (sas)
        {
            // The announcement: queued and $56, nothing paid yet (QSP SendHitOnly).
            QueueProgressiveWin(level, cents, sas: true);
            RaiseException(SasExceptionCode.SASProgressiveLevelHit);
            lock (_gate)
                _winPendingDeadline = Deadline(ProgressiveWindow);
        }

        // Above the jackpot limit the award is the round's hand pay: a level the machine runs is queued without $54 (QSP SendHitOnly, libsas_wrapper.cpp:3867).
        if (ProgressiveIsHandpaid(level, cents, sas))
        {
            if (!sas)
                QueueProgressiveWin(level, cents, sas: false);
            return cents;
        }

        // The award: machine-paid progressive win and coin out (QSP SasNetwork_Progressives.hpp:399, SasNetwork_Meters.hpp:361).
        long credits = cents / AccountingCents;
        Meters.Increment(SasMeterCode.TotalMachinePaidProgressiveWin_Crd, credits);
        Meters.Increment(SasMeterCode.TotalCoinOutCredits, credits);
        Meters.SetCurrentCredits(Meters.Read(SasMeterCode.CurrentCredits) + credits);

        // An announced win owes only $54 now; one the machine runs itself is queued with it (QSP SendHitAndCredit).
        if (!sas)
            QueueProgressiveWin(level, cents, sas: false);
        RaiseException(SasExceptionCode.ProgressiveWin);
        return cents;
    }

    /// <summary>sasSetProgressiveLevelWin's queue and most-recent slot (libsas_wrapper.cpp:3760 to :3839).</summary>
    private void QueueProgressiveWin(byte level, long cents, bool sas)
    {
        lock (_gate)
        {
            if (_progressiveWins.Count >= QueueCapacity)
                _progressiveWins.RemoveAt(0);
            _progressiveWins.Add(new ProgressiveWin(level, cents, sas));
            _lastProgressiveWin = (level, cents);
        }
    }

    /// <summary>The broadcast windows and the win-pending timer, once a turn of the polling loop.</summary>
    private void TickProgressives()
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_masterBroadcastDeadline != 0 && now >= _masterBroadcastDeadline)
            {
                _masterBroadcastDeadline = 0;
                BroadcastTimeoutLocked(0);
            }

            for (int level = 1; level < QueueCapacity; level++)
            {
                if (_levelDeadlines[level] != 0 && now >= _levelDeadlines[level])
                {
                    _levelDeadlines[level] = 0;
                    BroadcastTimeoutLocked(level);
                }
            }

            if (_winPendingDeadline != 0 && now >= _winPendingDeadline)
                WinPendingTickLocked();
        }
    }

    /// <summary>internalProgressiveBroadcastTimeout: $53 once a loss, and the level stale to QSP (SasNetwork_Callbacks.hpp:340).</summary>
    private void BroadcastTimeoutLocked(int level)
    {
        _staleLevels = StaleLevelsLocked() | (level == 0 ? SasLevels : (SasLevels & (1u << (level - 1))));
        WithdrawLockIfUnplayable();

        if (_broadcastsState != BroadcastsState.Counting)
            return;

        RaiseException(SasExceptionCode.NoProgressiveInfoReceived);
        _broadcastsState = BroadcastsState.TimedOut;
    }

    /// <summary>The win-pending timer: every third five-second tick raises $56 while SAS wins remain (libsas_threads.cpp:3148).</summary>
    private void WinPendingTickLocked()
    {
        _winPendingCounter++;
        if (_progressiveWins.Any(win => win.Sas))
        {
            if (_winPendingCounter == 3)
            {
                RaiseException(SasExceptionCode.SASProgressiveLevelHit);
                _winPendingCounter = 0;
            }

            _winPendingDeadline = Deadline(ProgressiveWindow);
        }
        else
        {
            _winPendingCounter = 0;
            _winPendingDeadline = 0;
        }
    }

    /// <summary>
    /// A restart: the timers are gone, every linked level waits for a broadcast again (QSP ConfigureLinkedProgressives),
    /// and SAS wins still queued raise $56 again (libsas_internal.cpp:876).
    /// </summary>
    private void RestartProgressivesLocked()
    {
        Array.Clear(_levelDeadlines);
        _masterBroadcastDeadline = 0;
        _staleLevels = SasLevels;
        _unacknowledgedProgressiveReads.Clear();
        _winPendingDeadline = 0;
        if (_progressiveWins.Any(win => win.Sas))
        {
            RaiseException(SasExceptionCode.SASProgressiveLevelHit);
            _winPendingDeadline = Deadline(ProgressiveWindow);
        }
    }

    /// <summary>A memory clear empties the library's progressive image (libsas_internal.cpp:756).</summary>
    private void ClearProgressivesLocked()
    {
        Array.Clear(_levelAmounts);
        _progressiveWins.Clear();
        _lastProgressiveWin = default;
        _broadcastsState = BroadcastsState.None;
        RestartProgressivesLocked();
    }

    private static long Deadline(TimeSpan after) => Stopwatch.GetTimestamp() + Ticks(after);

    private static string MachineProgressiveLevels(uint mask) =>
        string.Join(',', Enumerable.Range(1, 32).Where(level => (mask & (1u << (level - 1))) != 0));
}
