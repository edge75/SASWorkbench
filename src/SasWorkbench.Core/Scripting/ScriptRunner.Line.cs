using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The line itself: chirps, a poll without its wakeup bit, following the address, and repeats in time.</summary>
public sealed partial class ScriptRunner
{
    /// <summary>The slowest and fastest gap between two chirps read as the 200 ms cadence, for a host reading them off a busy computer.</summary>
    private static readonly TimeSpan ChirpGapLow = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan ChirpGapHigh = TimeSpan.FromMilliseconds(300);

    /// <summary>Listens to the quiet line for the chirp's cadence, or for nothing at all.</summary>
    private static StepResult ExpectChirp(ExpectChirpStep step, PollEngine engine, SasAddress? machine, TimeSpan at)
    {
        // The machine's own address, not whichever address the last poll was sent to.
        byte address = (machine ?? engine.Round[0]).Value;
        bool IsChirp(HeardMessage message) =>
            message.Wakeup && message.Frame.Length == 1 && message.Frame[0] == address;

        IReadOnlyList<HeardMessage>? heard = step.Negated
            ? engine.Listen(step.Within)
            : engine.Listen(step.Within + (ChirpGapHigh * ExpectChirpStep.Count),
                messages => messages.Count >= ExpectChirpStep.Count || messages.Any(m => !IsChirp(m)));

        if (heard is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: "a quiet line to listen to",
                Actual: "the host is polling a round on a timer",
                Reason: "a chirp is only heard on a line nobody polls. Put 'host poll sequence' before it.",
                At: at) { IsAssertion = true };
        }

        string Show() => heard.Count == 0
            ? "nothing"
            : string.Join(", ", heard.Select(m => $"{(m.Wakeup ? "wakeup " : string.Empty)}{Convert.ToHexString(m.Frame)} at {m.At.TotalMilliseconds:F0}ms"));

        if (step.Negated)
        {
            return new StepResult(step.Location, step.Describe(), heard.Count == 0 ? Verdict.Pass : Verdict.Fail,
                Expected: "a silent line", Actual: Show(), At: at) { IsAssertion = true };
        }

        string? wrong = heard.FirstOrDefault(m => !IsChirp(m)) is { } other
            ? $"{Convert.ToHexString(other.Frame)}{(other.Wakeup ? " with the wakeup bit" : " without the wakeup bit")} is not a chirp of ${address:X2}"
            : heard.Count < ExpectChirpStep.Count
                ? $"{heard.Count} chirp(s) where {ExpectChirpStep.Count} were waited for"
                : null;

        for (int i = 1; wrong is null && i < heard.Count; i++)
        {
            TimeSpan gap = heard[i].At - heard[i - 1].At;
            if (gap < ChirpGapLow || gap > ChirpGapHigh)
                wrong = $"chirps {gap.TotalMilliseconds:F0}ms apart, where the cadence is 200ms";
        }

        return new StepResult(step.Location, step.Describe(), wrong is null ? Verdict.Pass : Verdict.Fail,
            Expected: $"{ExpectChirpStep.Count} chirps of ${address:X2}, about 200ms apart",
            Actual: Show(),
            Reason: wrong,
            At: at) { IsAssertion = true };
    }

    /// <summary>Sends a long poll without its wakeup bit: silence or a refusal passes, a reply fails.</summary>
    private static StepResult SendWithoutWakeup(SendWithoutWakeupStep step, PollEngine engine, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Poll.Command, step.Poll.Data, withoutWakeup: true);
        bool held = result.Outcome is LongPollOutcome.Silent or LongPollOutcome.Rejected;
        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: "the poll ignored or refused, since nothing marked its start",
            Actual: result.ToString(),
            Reason: held ? null : result.Detail,
            At: at) { IsAssertion = true };
    }

    /// <summary>Sends a long poll with a broken check sequence: silence or a refusal passes, a reply fails.</summary>
    private static StepResult SendBadCrc(SendBadCrcStep step, PollEngine engine, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Poll.Command, step.Poll.Data, checkSequence: true, badCheckSequence: true);
        bool held = result.Outcome is LongPollOutcome.Silent or LongPollOutcome.Rejected;
        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: "the poll ignored or refused, since its check sequence is wrong",
            Actual: result.ToString(),
            Reason: held ? null : result.Detail,
            At: at) { IsAssertion = true };
    }

    /// <summary>Points the host at the machine's new address.</summary>
    private static StepResult Follow(FollowAddressStep step, ChannelHosts hosts, TimeSpan at)
    {
        string? refusal = hosts.Follow(step.Address);
        return new StepResult(step.Location, step.Describe(), refusal is null ? Verdict.Pass : Verdict.Error,
            Actual: refusal is null ? $"the host now polls ${step.Address}" : null,
            Reason: refusal,
            At: at);
    }

    /// <summary>
    /// Waits for the next report of an exception and checks it came a period after the last one,
    /// taking the first to arrive as the last when none had.
    /// </summary>
    private static StepResult CheckRepeat(
        ExpectExceptionRepeatStep step, PollEngine engine, SasAddress? machine, ReportedExceptions exceptions, TimeSpan at)
    {
        TimeSpan tolerance = TimeSpan.FromTicks(Math.Max(step.Period.Ticks / 4, engine.PollInterval.Ticks * 2));
        TimeSpan longest = step.Period + tolerance;

        ExceptionReport? last = exceptions.Latest(engine.Channel, machine, step.Code)
                                ?? Await(() => exceptions.Latest(engine.Channel, machine, step.Code), longest);
        if (last is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: SasNames.DescribeException(step.Code),
                Actual: $"none in {longest.TotalMilliseconds:F0}ms",
                At: at) { IsAssertion = true };
        }

        TimeSpan wait = last.Elapsed + longest - engine.Elapsed;
        ExceptionReport? next = Await(() => exceptions.After(engine.Channel, machine, step.Code, last.Sequence), wait);
        exceptions.Consume(last);
        if (next is not null)
            exceptions.Consume(next);

        TimeSpan? gap = next is null ? null : next.Elapsed - last.Elapsed;
        bool held = gap is { } g && g >= step.Period - tolerance && g <= longest;
        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: $"{step.Name} again {step.Period.TotalMilliseconds:F0}ms after the last, within {tolerance.TotalMilliseconds:F0}ms",
            Actual: gap is { } seen
                ? $"again {seen.TotalMilliseconds:F0}ms after the one at {last.Elapsed.TotalMilliseconds:F0}ms"
                : $"not again in {longest.TotalMilliseconds:F0}ms after the one at {last.Elapsed.TotalMilliseconds:F0}ms",
            At: at) { IsAssertion = true };
    }

    /// <summary>Polls a lookup until it finds something or the time runs out.</summary>
    private static ExceptionReport? Await(Func<ExceptionReport?> lookup, TimeSpan within)
    {
        long deadline = Environment.TickCount64 + (long)Math.Max(0, within.TotalMilliseconds);
        while (true)
        {
            if (lookup() is { } found)
                return found;
            if (Environment.TickCount64 >= deadline)
                return null;
            Thread.Sleep(5);
        }
    }
}
