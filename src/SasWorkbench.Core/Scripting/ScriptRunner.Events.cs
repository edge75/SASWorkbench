using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Scripting;

/// <summary>What a poll placed by a case drew, and in which format.</summary>
public sealed partial class ScriptRunner
{
    /// <summary>True when a general poll's answer came in the format the case asked for.</summary>
    private static bool InFormat(DrawFormat format, GeneralPollResult result) => format switch
    {
        DrawFormat.Event => result.IsRealTimeEvent,
        DrawFormat.Bare => !result.IsRealTimeEvent,
        _ => true,
    };

    private static string FormatWords(DrawFormat format) => format switch
    {
        DrawFormat.Event => " in the event format",
        DrawFormat.Bare => " as a single byte",
        _ => string.Empty,
    };

    /// <summary>Sends one long poll in a sequence and checks it was answered with the named event.</summary>
    /// <remarks>
    /// Refused outside a sequence for the reason a general poll placed by hand is: the round would draw
    /// the same event just as easily, and the assertion would say nothing about this poll.
    /// </remarks>
    private static StepResult SendForEvent(SendLongPollDrawsEventStep step, PollEngine engine, TimeSpan at)
    {
        string expected = $"event {step.CodeName} in this long poll's window{(step.OrSilence ? ", or silence" : string.Empty)}";
        if (!engine.IsSequenced)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: expected,
                Actual: "the host is polling a round on a timer",
                Reason: "the round's general polls drain the queue as fast as this poll could, so what it drew "
                        + "says nothing. Put 'host poll sequence' before it.",
                At: at) { IsAssertion = true };
        }

        LongPollResult result = engine.Execute(step.Poll.Command, step.Poll.Data);
        bool drew = result.Outcome == LongPollOutcome.AnsweredWithEvent
                    && result.Frame.Length > SasEventLayouts.HeaderLength
                    && result.Frame[SasEventLayouts.HeaderLength - 1] == step.Code;
        bool held = drew || (step.OrSilence && result.Outcome == LongPollOutcome.Silent);

        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: expected,
            Actual: result.ToString(),
            Reason: held ? null : result.Detail ?? "the long poll was answered with its own reply, not an event",
            At: at) { IsAssertion = true };
    }
}
