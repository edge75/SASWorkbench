using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>The progressive broadcasts a case keeps going in the background, and the answer either way.</summary>
public sealed partial class ScriptRunner
{
    /// <summary>Starts one background broadcast on the current host.</summary>
    private static StepResult StartBroadcast(BroadcastStep step, PollEngine engine, HostBroadcasts broadcasts, TimeSpan at)
    {
        broadcasts.Start(step, engine);
        return new StepResult(step.Location, step.Describe(), Verdict.Pass,
            Actual: $"{broadcasts.Count} broadcast(s) running", At: at);
    }

    /// <summary>Sends a long poll and passes on any answer, a refusal included, and fails on silence.</summary>
    private static StepResult ExpectAnswer(ExpectAnswerStep step, PollEngine engine, SasAddress? machine, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Command, step.Data, to: machine);
        bool answered = result.Outcome is LongPollOutcome.Answered or LongPollOutcome.Acknowledged or LongPollOutcome.Rejected;
        return new StepResult(step.Location, step.Describe(), answered ? Verdict.Pass : Verdict.Fail,
            Expected: "a reply, an acknowledgement or a refusal",
            Actual: result.ToString(),
            Reason: answered ? null : result.Detail,
            At: at) { IsAssertion = true };
    }

    /// <summary>Stops every background broadcast.</summary>
    private static StepResult StopBroadcasts(StopBroadcastsStep step, HostBroadcasts broadcasts, TimeSpan at)
    {
        int stopped = broadcasts.Count;
        broadcasts.Dispose();
        return new StepResult(step.Location, step.Describe(), Verdict.Pass,
            Actual: $"{stopped} broadcast(s) stopped", At: at);
    }

    /// <summary>
    /// Background senders of one broadcast each, sent to address 00 through the host's own engine so
    /// they queue behind whatever a step sends rather than interleaving with it.
    /// </summary>
    /// <remarks>
    /// None is sent while the case owns the line with a sequence: a message the case did not name
    /// would change what the sequence means. A broadcast nobody answers cannot fail, so the outcome
    /// is not looked at; the wire log shows every one sent.
    /// </remarks>
    private sealed class HostBroadcasts : IDisposable
    {
        private readonly List<(CancellationTokenSource Stop, Thread Thread)> _running = [];

        public int Count => _running.Count;

        public void Start(BroadcastStep step, PollEngine engine)
        {
            var stop = new CancellationTokenSource();
            var thread = new Thread(() => Run(step, engine, stop.Token))
            {
                IsBackground = true,
                Name = $"broadcast-{step.Send.Command:X2}",
            };
            _running.Add((stop, thread));
            thread.Start();
        }

        private static void Run(BroadcastStep step, PollEngine engine, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested && engine.IsRunning)
            {
                if (!engine.IsSequenced)
                    engine.Execute(step.Send.Command, step.Send.Data, purpose: "background broadcast", to: SasAddress.Sync);

                stop.WaitHandle.WaitOne(step.Every);
            }
        }

        public void Dispose()
        {
            foreach ((CancellationTokenSource stop, Thread _) in _running)
                stop.Cancel();
            foreach ((CancellationTokenSource stop, Thread thread) in _running)
            {
                thread.Join(TimeSpan.FromSeconds(5));
                stop.Dispose();
            }

            _running.Clear();
        }
    }
}
