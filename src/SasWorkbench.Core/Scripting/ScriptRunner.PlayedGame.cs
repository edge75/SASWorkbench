using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The busy reply and silence on a poll that carries its own length (D59).</summary>
public sealed partial class ScriptRunner
{
    /// <summary>Checks that a long poll was met with the busy reply, the machine's address and a single zero.</summary>
    private static StepResult ExpectBusy(ExpectBusyReplyStep step, PollEngine engine, SasAddress? machine, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Command, step.Data, to: machine);
        bool busy = result.Outcome == LongPollOutcome.Answered
                    && result.Frame.Length == 2
                    && result.Frame[1] == HostMessage.BusyResponseByte;

        return new StepResult(
            step.Location,
            step.Describe(),
            busy ? Verdict.Pass : Verdict.Fail,
            Expected: "the address followed by 00",
            Actual: result.ToString(),
            Reason: busy ? null : result.Detail ?? "the machine served the poll, or refused it, rather than answering busy",
            At: at)
        {
            IsAssertion = true,
        };
    }

    /// <summary>
    /// Whether a poll whose length the machine reads out of the message gives a length its data matches, so the
    /// receiver frames it whole and any silence after it comes from a handler.
    /// </summary>
    private static bool CarriesItsLength(byte[] data) => data.Length >= 1 && data[0] == data.Length - 1;
}
