using System.Diagnostics;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>How a run should behave.</summary>
public sealed record ScriptRunnerOptions
{
    /// <summary>What should drive the gaming machine.</summary>
    public StimulusBackend Stimulus { get; init; } = StimulusBackend.None;

    /// <summary>Asks a person to do something, for the manual backend.</summary>
    public StimulusPrompt? AskPerson { get; init; }

    /// <summary>How long a person is given to answer a manual prompt before the step is skipped.</summary>
    public TimeSpan PromptDeadline { get; init; } = ManualStimulusBackend.DefaultDeadline;

    /// <summary>Whether the host waits for the machine to connect or dials it.</summary>
    public TcpRole TcpRole { get; init; } = TcpRole.Listen;

    /// <summary>How long to wait for a peer to appear before skipping the test.</summary>
    public TimeSpan PeerTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The poll interval a script has not overridden.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How long to wait for the machine to answer one poll.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a quiet line means a reply has ended.</summary>
    public TimeSpan QuietGap { get; init; } = TimeSpan.FromMilliseconds(20);

    /// <summary>Builds the link and the stimulus. Defaults to a real link with no stimulus.</summary>
    public EnvironmentFactory Environment { get; init; } = DefaultEnvironment.Create;

    /// <summary>Called as each test finishes, so a caller can print progress.</summary>
    public Action<TestOutcome>? OnTestFinished { get; init; }

    /// <summary>
    /// Where to record the run as one ordered stream. Supplying one lets a caller watch a run
    /// live, and puts the run's steps on the same clock as the wire it produced.
    /// </summary>
    public SessionLog? Log { get; init; }
}

/// <summary>
/// Runs tests: opens the link, drives the machine, and turns each claim into a verdict.
/// </summary>
/// <remarks>
/// <para>
/// A relative meter assertion is measured against a baseline, and the baseline is refreshed
/// immediately before every stimulus step. That is what makes <c>expect meter total_bill_in
/// += 2000</c> mean "two thousand more than before the note went in" without the script having
/// to say so.
/// </para>
/// <para>
/// Exceptions are consumed as they are matched, so two assertions for the same exception need two
/// occurrences of it. Without that, one door opening would satisfy every assertion in the file
/// that mentioned a door.
/// </para>
/// </remarks>
public sealed class ScriptRunner
{
    private readonly ScriptRunnerOptions _options;

    public ScriptRunner(ScriptRunnerOptions? options = null)
    {
        _options = options ?? new ScriptRunnerOptions();
        Log = _options.Log ?? new SessionLog();
    }

    /// <summary>Everything this runner did, in order, on one clock.</summary>
    public SessionLog Log { get; }

    /// <summary>Runs every test in every document.</summary>
    public RunReport RunAll(IEnumerable<ScriptDocument> documents, CancellationToken cancel = default)
    {
        var stopwatch = Stopwatch.StartNew();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var results = new List<TestOutcome>();
        var provenance = new List<RunProvenance>();
        CapabilitySet capabilities = CapabilityProbe.Baseline();

        foreach (ScriptDocument document in documents)
        {
            foreach (ScriptTest test in document.Tests)
            {
                cancel.ThrowIfCancellationRequested();
                TestOutcome result = Run(test, capabilities, out RunProvenance? how);
                results.Add(result);
                _options.OnTestFinished?.Invoke(result);

                if (how is not null && !provenance.Contains(how))
                    provenance.Add(how);
            }
        }

        return new RunReport
        {
            Tests = results,
            StartedAt = startedAt,
            Duration = stopwatch.Elapsed,
            Provenance = provenance,
            Capabilities = capabilities.Names,
        };
    }

    /// <summary>Runs one test.</summary>
    public TestOutcome Run(ScriptTest test) => Run(test, CapabilityProbe.Baseline(), out _);

    private TestOutcome Run(ScriptTest test, CapabilitySet baseline, out RunProvenance? provenance)
    {
        provenance = null;

        // A test measures from its own start; the session outlives every test in the run. The base
        // is taken with the stopwatch so the two zeroes are the same instant.
        ConnectStep? connect = test.Steps.OfType<ConnectStep>().FirstOrDefault();
        ClockBase testBase = Log.Clock.Mark();
        var stopwatch = Stopwatch.StartNew();
        Log.TestStarted(test.Name, test.Location, connect?.Channel ?? SasChannel.One);

        if (connect is null)
        {
            // A case written from a specification, long before the machine can be driven far
            // enough to run it, has neither a link nor the capability it needs. What it needs is
            // the more useful of the two answers, and it is a skip rather than a broken test.
            if (baseline.FirstUnmet(test.Requirements) is { } missing)
                return Skipped(test, stopwatch, $"{missing.Requirement.Key}: {missing.Reason}",
                    SasChannel.One, testBase);

            var broken = new TestOutcome
            {
                Name = test.Name,
                Location = test.Location,
                Verdict = Verdict.Error,
                Tags = test.Tags,
                Duration = stopwatch.Elapsed,
                SkipReason = "the test never opens a link; add 'host connect ...'",
                Steps =
                [
                    new StepResult(test.Location, test.Name, Verdict.Error,
                        Reason: "no 'host connect' step"),
                ],
            };

            Log.TestFinished(broken, SasChannel.One);
            return broken;
        }

        ScriptEnvironment environment;
        try
        {
            environment = _options.Environment(connect, _options);
        }
        catch (TransportRefusedException refused)
        {
            // No environment came up, so nothing beyond the baseline can be offered. If the test
            // was going to be skipped for a missing capability anyway, that is the reason worth
            // reporting - "no peer on the link" would send somebody looking for the wrong thing.
            if (baseline.FirstUnmet(test.Requirements) is { } missing)
                return Skipped(test, stopwatch, $"{missing.Requirement.Key}: {missing.Reason}",
                    connect.Channel, testBase);

            return Skipped(test, stopwatch, refused.Message, connect.Channel, testBase);
        }

        using (environment)

        {
            CapabilitySet capabilities = baseline
                .With(environment.ExtraCapabilities)
                .With($"transport.{environment.HostTransport.Capabilities.Kind}"
                    .ToLowerInvariant(), "yes");

            provenance = new RunProvenance(
                environment.HostTransport.Description,
                environment.HostTransport.Capabilities.Fidelity,
                environment.Stimulus.Kind,
                environment.HostTransport.Capabilities.Caveat);

            (Requirement Requirement, string Reason)? unmet = capabilities.FirstUnmet(test.Requirements);
            if (unmet is { } gap)
            {
                return Skipped(test, stopwatch, $"{gap.Requirement.Key}: {gap.Reason}",
                    connect.Channel, testBase, provenance);
            }

            return Execute(test, connect, environment, stopwatch, testBase, provenance);
        }
    }

    private TestOutcome Execute(
        ScriptTest test,
        ConnectStep connect,
        ScriptEnvironment environment,
        Stopwatch stopwatch,
        ClockBase testBase,
        RunProvenance provenance)
    {
        TimeSpan interval = test.Steps.OfType<PollEveryStep>().FirstOrDefault()?.Interval
                            ?? _options.PollInterval;

        var engineOptions = new PollEngineOptions
        {
            Address = connect.Address,
            Channel = connect.Channel,
            PollInterval = interval,
            ResponseTimeout = _options.ResponseTimeout,
            QuietGap = _options.QuietGap,
        };

        PollEngine engine;
        try
        {
            engine = new PollEngine(environment.HostTransport, engineOptions);
        }
        catch (TransportRefusedException refused)
        {
            return Skipped(test, stopwatch, refused.Message, connect.Channel, testBase, provenance);
        }

        // The engine keeps its own clock, so its zero is marked on the session clock before it
        // starts and every record it produces is converted before joining the stream.
        ClockBase engineBase = Log.Clock.Mark();
        engine.Wire += (direction, channel, symbols, elapsed, note) =>
            Log.Wire(direction, channel, symbols, elapsed, engineBase, note);
        engine.LinkStateChanged += state =>
            Log.Link(state, connect.Channel, engineBase, engine.Elapsed);

        var exceptions = new List<ExceptionReport>();
        var consumed = new HashSet<long>();
        engine.ExceptionReported += report =>
        {
            Log.Exception(report, engineBase, SasNames.ExceptionName(report.Code));
            lock (exceptions)
                exceptions.Add(report);
        };

        var steps = new List<StepResult>();
        var baselines = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var window = new LastStimulus();

        using (engine)
        {
            try
            {
                engine.Start();
            }
            catch (TransportRefusedException refused)
            {
                return Skipped(test, stopwatch, refused.Message, connect.Channel, testBase, provenance);
            }

            if (environment.HostTransport is TcpTransport tcp && !tcp.WaitForPeer(_options.PeerTimeout))
            {
                return Skipped(test, stopwatch,
                    $"no gaming machine connected to {tcp.Description} within "
                    + $"{_options.PeerTimeout.TotalSeconds:F0}s",
                    connect.Channel, testBase, provenance);
            }

            RefreshBaselines(engine, test.WatchedMeters, baselines);

            foreach (ScriptStep step in test.Steps)
            {
                StepResult result = RunStep(step, engine, environment, exceptions, consumed,
                    test, baselines, window, stopwatch.Elapsed);
                steps.Add(result);
                Log.Step(result, connect.Channel, testBase);

                if (result.Verdict == Verdict.Error)
                    break;
            }

            IReadOnlyList<WireRecord> wire = engine.WireLog.Snapshot();
            engine.Stop();

            var outcome = new TestOutcome
            {
                Name = test.Name,
                Location = test.Location,
                Verdict = Aggregate(steps),
                Steps = steps,
                Duration = stopwatch.Elapsed,
                Tags = test.Tags,
                Wire = wire,
                Provenance = provenance,
                SkipReason = Aggregate(steps) == Verdict.Skip
                    ? steps.FirstOrDefault(s => s.Verdict == Verdict.Skip)?.Reason
                    : null,
            };

            Log.TestFinished(outcome, connect.Channel);
            return outcome;
        }
    }

    private StepResult RunStep(
        ScriptStep step,
        PollEngine engine,
        ScriptEnvironment environment,
        List<ExceptionReport> exceptions,
        HashSet<long> consumed,
        ScriptTest test,
        Dictionary<string, long> baselines,
        LastStimulus window,
        TimeSpan at)
    {
        try
        {
            return step switch
            {
                ConnectStep or PollEveryStep => new StepResult(
                    step.Location, step.Describe(), Verdict.Pass, At: at),

                NoteStep note => new StepResult(
                    note.Location, note.Describe(), Verdict.Pass, At: at),

                WaitStep wait => Wait(wait, at),

                SendLongPollStep send => Send(send, engine, at),

                ForgetExceptionsStep forget => Forget(forget, engine, exceptions, consumed, at),

                StimulusStep stimulus => ApplyStimulus(
                    stimulus, engine, environment, test, baselines, window, at),

                ExpectExceptionStep expect => CheckException(
                    expect, exceptions, consumed, window, at),

                ExpectMeterStep expect => CheckMeter(expect, engine, baselines, at),

                ExpectFieldStep expect => CheckField(expect, engine, at),

                ExpectLinkStep expect => CheckLink(expect, engine, at),

                _ => new StepResult(step.Location, step.Describe(), Verdict.Error,
                    Reason: $"the runner does not implement '{step.GetType().Name}'", At: at),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Error,
                Reason: $"{ex.GetType().Name}: {ex.Message}", At: at);
        }
    }

    /// <summary>
    /// When the machine was last acted on, and what was done to it.
    /// </summary>
    /// <remarks>
    /// Used only to explain a failure, never to decide one. An exception's timestamp is when the
    /// host read it off the wire, not when the machine raised it, and the two can be a whole poll
    /// cycle apart — so filtering assertions by that time would quietly change what a test means
    /// depending on how fast the link is. It is still the most useful thing to say to somebody
    /// looking at a negated expectation that failed on an exception an earlier step caused.
    /// </remarks>
    private sealed class LastStimulus
    {
        public TimeSpan At { get; set; } = TimeSpan.Zero;

        public string? Description { get; set; }
    }

    /// <summary>
    /// Says when a negated expectation has probably caught an earlier step's exception.
    /// </summary>
    /// <remarks>
    /// A machine reports exceptions one per poll, so a step that raises ten leaves nine of them
    /// arriving over the next two seconds — long after the step that caused them has finished.
    /// Somebody reading the failure needs to be told that, and told what to write instead.
    /// </remarks>
    private static string? Blame(ExceptionReport match, LastStimulus window)
    {
        if (window.Description is null || match.Elapsed >= window.At)
            return null;

        return $"it arrived before '{window.Description}' began, so an earlier step raised it. "
            + "Put 'host forget exceptions' after that step to leave its exceptions out of what "
            + "follows.";
    }

    /// <summary>
    /// Waits for the machine to run out of things to say, then leaves all of it out.
    /// </summary>
    /// <remarks>
    /// The waiting is the point. A machine reports one exception per poll, so at the moment this
    /// step runs, most of what the setting-up caused is still queued inside the machine and has
    /// not been read yet — forgetting only what has arrived would leave the rest to fail an
    /// assertion a second later. Quiet for three poll cycles is taken as the queue being empty,
    /// which is what the machine reporting nothing on three consecutive polls means.
    /// </remarks>
    private static StepResult Forget(
        ForgetExceptionsStep step,
        PollEngine engine,
        List<ExceptionReport> exceptions,
        HashSet<long> consumed,
        TimeSpan at)
    {
        TimeSpan quiet = engine.PollInterval * 3;
        long deadline = Environment.TickCount64 + (long)step.Within.TotalMilliseconds;
        int seen = Count(exceptions);
        long lastChange = Environment.TickCount64;

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(5);
            int now = Count(exceptions);
            if (now != seen)
            {
                seen = now;
                lastChange = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastChange >= (long)quiet.TotalMilliseconds)
                break;
        }

        int forgotten = 0;
        lock (exceptions)
        {
            foreach (ExceptionReport report in exceptions)
            {
                if (consumed.Add(report.Sequence))
                    forgotten++;
            }
        }

        return new StepResult(step.Location, step.Describe(), Verdict.Pass,
            Actual: $"{forgotten} exception(s) left out of what follows", At: at);
    }

    private static int Count(List<ExceptionReport> exceptions)
    {
        lock (exceptions)
            return exceptions.Count;
    }

    private static StepResult Wait(WaitStep step, TimeSpan at)
    {
        Thread.Sleep(step.Duration);
        return new StepResult(step.Location, step.Describe(), Verdict.Pass, At: at);
    }

    private static StepResult Send(SendLongPollStep step, PollEngine engine, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Command, step.Data);
        Verdict verdict = result.Succeeded ? Verdict.Pass : Verdict.Fail;

        return new StepResult(
            step.Location,
            step.Describe(),
            verdict,
            Expected: "a reply",
            Actual: result.ToString(),
            Reason: result.Detail,
            At: at)
        {
            IsAssertion = true,
        };
    }

    private StepResult ApplyStimulus(
        StimulusStep step,
        PollEngine engine,
        ScriptEnvironment environment,
        ScriptTest test,
        Dictionary<string, long> baselines,
        LastStimulus window,
        TimeSpan at)
    {
        // Take the baseline before acting, so a relative assertion afterwards measures the effect
        // of this step and not of everything since the test began.
        RefreshBaselines(engine, test.WatchedMeters, baselines);
        window.At = at;
        window.Description = step.Describe();

        try
        {
            environment.Stimulus.Apply(step);
            return new StepResult(step.Location, step.Describe(), Verdict.Pass, At: at);
        }
        catch (StimulusUnavailableException unavailable)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: unavailable.Reason, At: at);
        }
    }

    private static StepResult CheckException(
        ExpectExceptionStep step,
        List<ExceptionReport> exceptions,
        HashSet<long> consumed,
        LastStimulus window,
        TimeSpan at)
    {
        long deadline = Environment.TickCount64 + (long)step.Within.TotalMilliseconds;
        ExceptionReport? match = null;

        while (true)
        {
            lock (exceptions)
            {
                match = exceptions.FirstOrDefault(
                    e => e.Code == step.Code && !consumed.Contains(e.Sequence));
            }

            if (match is not null || Environment.TickCount64 >= deadline)
                break;

            Thread.Sleep(5);
        }

        if (step.Negated)
        {
            return match is null
                ? new StepResult(step.Location, step.Describe(), Verdict.Pass,
                    Expected: $"no {step.Name}", Actual: "none arrived", At: at) { IsAssertion = true }
                : new StepResult(step.Location, step.Describe(), Verdict.Fail,
                    Expected: $"no {step.Name}",
                    Actual: $"{step.Name} arrived at {match.Elapsed.TotalMilliseconds:F0}ms",
                    Reason: Blame(match, window),
                    At: at) { IsAssertion = true };
        }

        if (match is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: SasNames.DescribeException(step.Code),
                Actual: $"nothing in {step.Within.TotalMilliseconds:F0}ms",
                At: at) { IsAssertion = true };
        }

        consumed.Add(match.Sequence);
        return new StepResult(step.Location, step.Describe(), Verdict.Pass,
            Expected: SasNames.DescribeException(step.Code),
            Actual: $"arrived at {match.Elapsed.TotalMilliseconds:F0}ms",
            At: at) { IsAssertion = true };
    }

    private static StepResult CheckMeter(
        ExpectMeterStep step,
        PollEngine engine,
        Dictionary<string, long> baselines,
        TimeSpan at)
    {
        if (!MeterIndex.TryResolve(step.Meter, out MeterSource source))
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: $"this build cannot read the meter '{step.Meter}'", At: at);
        }

        long deadline = Environment.TickCount64 + (long)step.Within.TotalMilliseconds;
        long? actual = null;
        string? failure = null;

        while (true)
        {
            (actual, failure) = ReadMeter(engine, source);
            if (actual is not null && Holds(step, baselines, actual.Value))
                break;
            if (Environment.TickCount64 >= deadline)
                break;
            Thread.Sleep(10);
        }

        if (actual is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(), Actual: failure ?? "the meter could not be read",
                Reason: failure, At: at) { IsAssertion = true };
        }

        long baseline = baselines.GetValueOrDefault(step.Meter);
        bool held = Holds(step, baselines, actual.Value);

        string expected = step.Comparison switch
        {
            MeterComparison.IncreasedBy => $"{baseline} + {step.Value} = {baseline + step.Value}",
            MeterComparison.IncreasedByAtLeast => $"at least {baseline + step.Value}",
            MeterComparison.Unchanged => $"{baseline}, unchanged",
            MeterComparison.Equals => step.Value.ToString(),
            MeterComparison.AtLeast => $"at least {step.Value}",
            MeterComparison.AtMost => $"at most {step.Value}",
            _ => step.Value.ToString(),
        };

        return new StepResult(
            step.Location,
            step.Describe(),
            held ? Verdict.Pass : Verdict.Fail,
            Expected: expected,
            Actual: $"{actual.Value} (read from lp ${source.Command:X2})",
            At: at) { IsAssertion = true };
    }

    private static bool Holds(
        ExpectMeterStep step,
        Dictionary<string, long> baselines,
        long actual)
    {
        long baseline = baselines.GetValueOrDefault(step.Meter);
        return step.Comparison switch
        {
            MeterComparison.IncreasedBy => actual - baseline == step.Value,
            MeterComparison.IncreasedByAtLeast => actual - baseline >= step.Value,
            MeterComparison.Unchanged => actual == baseline,
            MeterComparison.Equals => actual == step.Value,
            MeterComparison.AtLeast => actual >= step.Value,
            MeterComparison.AtMost => actual <= step.Value,
            _ => false,
        };
    }

    private static StepResult CheckField(ExpectFieldStep step, PollEngine engine, TimeSpan at)
    {
        SasMessageLayout? layout = SasLayouts.For(step.Command);
        if (layout is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: $"this build cannot decode the reply to long poll ${step.Command:X2}", At: at);
        }

        LongPollResult reply = engine.Execute(step.Command);
        if (!reply.Succeeded)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(), Actual: reply.ToString(), Reason: reply.Detail, At: at)
            { IsAssertion = true };
        }

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, reply.Frame);
        if (!decoded.Succeeded)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(), Actual: reply.ToHex(), Reason: decoded.Failure, At: at)
            { IsAssertion = true };
        }

        SasValue? value = decoded[step.Field];
        if (value is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(),
                Actual: $"the reply held no '{step.Field}'; it held "
                        + string.Join(", ", decoded.Paths),
                At: at) { IsAssertion = true };
        }

        bool held;
        string actualText;

        if (step.ExpectedText is not null)
        {
            actualText = value.Text ?? value.ToHex();
            bool same = string.Equals(actualText, step.ExpectedText, StringComparison.Ordinal);
            held = step.Comparison == SasComparison.Equal ? same : !same;
        }
        else if (value.Number is { } number)
        {
            actualText = number.ToString();
            held = Compare(number, step.Comparison, step.ExpectedNumber!.Value);
        }
        else
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(),
                Actual: $"'{step.Field}' is {value.ToHex()}, which is not a number",
                At: at) { IsAssertion = true };
        }

        return new StepResult(
            step.Location,
            step.Describe(),
            held ? Verdict.Pass : Verdict.Fail,
            Expected: step.ExpectedText ?? step.ExpectedNumber?.ToString(),
            Actual: actualText,
            At: at) { IsAssertion = true };
    }

    private static StepResult CheckLink(ExpectLinkStep step, PollEngine engine, TimeSpan at)
    {
        long deadline = Environment.TickCount64 + (long)step.Within.TotalMilliseconds;
        while (engine.State != step.Expected && Environment.TickCount64 < deadline)
            Thread.Sleep(5);

        bool held = engine.State == step.Expected;
        return new StepResult(
            step.Location,
            step.Describe(),
            held ? Verdict.Pass : Verdict.Fail,
            Expected: step.Expected.ToString(),
            Actual: engine.State.ToString(),
            At: at) { IsAssertion = true };
    }

    private static bool Compare(long actual, SasComparison comparison, long expected) => comparison switch
    {
        SasComparison.Equal => actual == expected,
        SasComparison.NotEqual => actual != expected,
        SasComparison.LessThan => actual < expected,
        SasComparison.AtMost => actual <= expected,
        SasComparison.GreaterThan => actual > expected,
        SasComparison.AtLeast => actual >= expected,
        SasComparison.BitsSet => (actual & expected) == expected,
        SasComparison.BitsClear => (actual & expected) == 0,
        _ => false,
    };

    private static void RefreshBaselines(
        PollEngine engine,
        IReadOnlyList<string> meters,
        Dictionary<string, long> baselines)
    {
        foreach (string meter in meters)
        {
            if (!MeterIndex.TryResolve(meter, out MeterSource source))
                continue;

            (long? value, _) = ReadMeter(engine, source);
            if (value is not null)
                baselines[meter] = value.Value;
        }
    }

    private static (long? Value, string? Failure) ReadMeter(PollEngine engine, MeterSource source)
    {
        SasMessageLayout? layout = SasLayouts.For(source.Command);
        if (layout is null)
            return (null, $"long poll ${source.Command:X2} has no layout");

        LongPollResult reply = engine.Execute(source.Command);
        if (!reply.Succeeded)
            return (null, reply.Detail ?? reply.Outcome.ToString());

        SasDecoded decoded = SasMessageDecoder.DecodeResponse(layout, reply.Frame);
        if (!decoded.Succeeded)
            return (null, decoded.Failure);

        long? value = decoded.Number(source.Field);
        return value is null
            ? (null, $"the reply held no '{source.Field}'")
            : (value, null);
    }

    private static Verdict Aggregate(IReadOnlyList<StepResult> steps)
    {
        if (steps.Any(s => s.Verdict == Verdict.Error))
            return Verdict.Error;
        if (steps.Any(s => s.Verdict == Verdict.Fail))
            return Verdict.Fail;

        // A test that made no claim at all has not passed. Reporting it green would be exactly
        // the silent pass this project refuses.
        if (!steps.Any(s => s.IsAssertion && s.Verdict == Verdict.Pass))
            return Verdict.Skip;

        return steps.Any(s => s.Verdict == Verdict.Skip) ? Verdict.Skip : Verdict.Pass;
    }

    /// <summary>
    /// A test that could not run. The verdict still joins the stream, because a test that started
    /// and never finished would leave the log looking as though it were still going.
    /// </summary>
    private TestOutcome Skipped(
        ScriptTest test,
        Stopwatch stopwatch,
        string reason,
        SasChannel channel,
        ClockBase testBase,
        RunProvenance? provenance = null)
    {
        var outcome = new TestOutcome
        {
            Name = test.Name,
            Location = test.Location,
            Verdict = Verdict.Skip,
            Tags = test.Tags,
            Duration = stopwatch.Elapsed,
            SkipReason = reason,
            Provenance = provenance,
            Steps = [new StepResult(test.Location, test.Name, Verdict.Skip, Reason: reason)],
        };

        Log.TestFinished(outcome, channel);
        return outcome;
    }
}
