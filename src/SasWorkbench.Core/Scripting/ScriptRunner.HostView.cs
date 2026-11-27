using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The host as a stimulus backend may read it.</summary>
public sealed partial class ScriptRunner
{
    private sealed class RunnerHostView(PollEngine engine, SasAddress? machine, ReportedExceptions exceptions)
        : IHostView
    {
        /// <summary>How long a long poll waits for the host's own round to put a general poll after it.</summary>
        private static readonly TimeSpan RoundWait = TimeSpan.FromSeconds(2);

        // A sequenced case decides every poll on the line, so a read of ours would be one it never asked for.
        public string? Unavailable => engine.IsSequenced
            ? "the case polls the machine in its own sequence, so the host cannot read anything to confirm the step "
              + "without changing what the case measures"
            : null;

        public long Mark() => exceptions.LastSequence(engine.Channel, machine);

        public ExceptionReport? ExceptionAfter(byte code, long mark, TimeSpan within)
        {
            long deadline = Environment.TickCount64 + (long)within.TotalMilliseconds;
            while (true)
            {
                if (exceptions.After(engine.Channel, machine, code, mark) is { } heard)
                    return heard;
                if (Environment.TickCount64 >= deadline)
                    return null;
                Thread.Sleep(20);
            }
        }

        public LongPollResult Ask(byte command, byte[] data)
        {
            LongPollResult result = engine.Execute(command, data, to: machine);

            // Two long polls with one command byte and nothing between are read as a retransmission (libsas#248),
            // so the case's next one must not follow ours directly.
            long after = engine.RoundsCompleted;
            long deadline = Environment.TickCount64 + (long)RoundWait.TotalMilliseconds;
            while (engine.RoundsCompleted == after && Environment.TickCount64 < deadline)
                Thread.Sleep(10);

            return result;
        }
    }
}
