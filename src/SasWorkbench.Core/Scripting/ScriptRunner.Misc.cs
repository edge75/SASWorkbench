using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The ROM signature, which arrives unasked on a later poll.</summary>
public sealed partial class ScriptRunner
{
    /// <summary>Every ROM signature a host heard, each read once.</summary>
    private sealed class RomSignatures
    {
        private readonly Lock _gate = new();
        private readonly List<RomSignatureReport> _all = [];
        private int _read;

        public void Add(RomSignatureReport report)
        {
            lock (_gate)
                _all.Add(report);
        }

        /// <summary>
        /// The first unread signature on that channel from that machine, which marks it and every earlier one read,
        /// so a signature the machine sent twice to a repeated poll is one signature.
        /// </summary>
        public RomSignatureReport? Take(SasChannel channel, SasAddress? machine)
        {
            lock (_gate)
            {
                for (int i = _read; i < _all.Count; i++)
                {
                    RomSignatureReport report = _all[i];
                    if (report.Channel != channel || (machine is not null && report.Address != machine))
                        continue;

                    _read = i + 1;
                    return report;
                }

                return null;
            }
        }
    }

    /// <summary>Waits for a ROM signature, or for none.</summary>
    private static StepResult ExpectRomSignature(
        ExpectRomSignatureStep step, PollEngine engine, SasAddress? machine, RomSignatures signatures, TimeSpan at)
    {
        long deadline = Environment.TickCount64 + (long)step.Within.TotalMilliseconds;
        RomSignatureReport? heard;
        while ((heard = signatures.Take(engine.Channel, machine)) is null && Environment.TickCount64 < deadline)
            Thread.Sleep(5);

        bool held = step.Negated ? heard is null : heard is not null;
        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: step.Negated ? "no ROM signature" : "a ROM signature",
            Actual: heard?.ToString() ?? $"nothing in {step.Within.TotalMilliseconds:F0}ms",
            Reason: held ? null : step.Negated
                ? "the machine sent a signature nobody had asked for since the last one was read"
                : "a signature answers a general poll, or a long poll while reporting is on and an exception is queued; none came",
            At: at) { IsAssertion = true };
    }
}
