using System.Diagnostics;
using SasWorkbench.Core.Tickets;
using System.Globalization;
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

    /// <summary>
    /// The directory the machine's printer files its tickets in, and the one this host reads.
    /// </summary>
    /// <remarks>
    /// The default is the wallet the peripheral simulator uses, because the two have to agree:
    /// a host reading one directory while the machine writes another finds nothing and reports it
    /// as a machine that did not print.
    /// </remarks>
    public string WalletDirectory { get; init; } = TicketWallet.DefaultDirectory;

    /// <summary>
    /// Whether this run may ask the machine to report exceptions in the real-time event format.
    /// </summary>
    /// <remarks>
    /// Off unless somebody asks for it. Long poll <c>$0E</c> changes how a machine answers every
    /// general poll after it, so a run that turned it on to find out whether it could would have
    /// changed what every case after that measured. The software machine offers the capability of
    /// its own accord, because that machine is one this build made and knows.
    /// </remarks>
    public bool RealTimeEventsAllowed { get; init; }

    /// <summary>
    /// The directory a ticket is dropped into to feed it to the machine's acceptor.
    /// </summary>
    /// <remarks>
    /// The other half of the wallet, and the same agreement applies: the machine's peripheral
    /// manager watches exactly this path, so a run writing somewhere else offers a ticket nobody
    /// is looking for and reports a machine that ignored it.
    /// </remarks>
    public string AcceptorDirectory { get; init; } = TicketAcceptor.DefaultDirectory;
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
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Log.TestStarted(test.Name, test.Location, connect?.Channel ?? SasChannel.One);

        if (connect is null)
        {
            // A case written from a specification, long before the machine can be driven far
            // enough to run it, has neither a link nor the capability it needs. What it needs is
            // the more useful of the two answers, and it is a skip rather than a broken test.
            // Nothing was built, so nothing was measured - but what the run's stimulus backend
            // would have offered is knowable without a link, and naming a capability it does
            // provide would send the reader after the wrong thing.
            CapabilitySet withBackend =
                baseline.With(CapabilityProbe.ForStimulus(_options.Stimulus));

            if (withBackend.FirstUnmet(Local(Declared(test))) is { } missing)
                return Skipped(test, stopwatch, startedAt, $"{missing.Requirement.Key}: {missing.Reason}",
                    SasChannel.One, testBase);

            var broken = new TestOutcome
            {
                Name = test.Name,
                Location = test.Location,
                Verdict = Verdict.Error,
                Tags = test.Tags,
                Requirements = test.Requirements,
                Machine = test.Machine,
                StartedAt = startedAt,
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
            environment = _options.Environment(connect, _options, test.Machine);
        }
        catch (TransportRefusedException refused)
        {
            // No environment came up, so nothing beyond the baseline can be offered. If the test
            // was going to be skipped for a missing capability anyway, that is the reason worth
            // reporting - "no peer on the link" would send somebody looking for the wrong thing.
            if (baseline.FirstUnmet(Local(Declared(test))) is { } missing)
                return Skipped(test, stopwatch, startedAt, $"{missing.Requirement.Key}: {missing.Reason}",
                    connect.Channel, testBase);

            return Skipped(test, stopwatch, startedAt, refused.Message, connect.Channel, testBase);
        }

        using (environment)

        {
            CapabilitySet capabilities = baseline
                .With(environment.ExtraCapabilities)
                .With(CapabilityProbe.ForWallet(_options.WalletDirectory))
                .With($"transport.{environment.HostTransport.Capabilities.Kind}"
                    .ToLowerInvariant(), "yes");

            // Opted into by name rather than probed. Finding out whether a machine honours
            // real-time event reporting means turning it on, and turning it on changes how that
            // machine answers every general poll afterwards — so a run says so on the command
            // line, and a run that does not leaves every case needing it skipping.
            if (_options.RealTimeEventsAllowed)
                capabilities = capabilities.With("stimulus.real_time_events", "yes");

            provenance = new RunProvenance(
                environment.HostTransport.Description,
                environment.HostTransport.Capabilities.Fidelity,
                environment.Stimulus.Kind,
                environment.HostTransport.Capabilities.Caveat);

            (Requirement Requirement, string Reason)? unmet =
                capabilities.FirstUnmet(Local(Declared(test)));
            if (unmet is { } gap)
            {
                return Skipped(test, stopwatch, startedAt, $"{gap.Requirement.Key}: {gap.Reason}",
                    connect.Channel, testBase, provenance);
            }

            return Execute(
                test, connect, environment, capabilities, stopwatch, startedAt, testBase, provenance);
        }
    }

    /// <summary>
    /// The requirements that can be settled without asking the machine anything.
    /// </summary>
    /// <remarks>
    /// The rest are checked once the link is up, because the machine is the only thing that can
    /// answer them. Checking them beforehand would skip every such case with "this environment
    /// provides no ..." — true of the environment, and not the question the case is asking.
    /// </remarks>
    private static IEnumerable<Requirement> Local(IEnumerable<Requirement> requirements) =>
        requirements.Where(r => !CapabilityProbe.IsAskedOfTheMachine(r.Key));

    /// <summary>
    /// Everything the case needs, including what it declared rather than required.
    /// </summary>
    /// <remarks>
    /// A declaration is checked by the same path as a requirement so that an unmet one is a skip
    /// with a reason and never a failure, which is what makes it D19 applied to a unit. It is kept
    /// out of the case's own requirement list so a report says what the script said, rather than
    /// showing a line nobody wrote.
    /// </remarks>
    private static IEnumerable<Requirement> Declared(ScriptTest test) =>
        test.Machine is { } machine
            ? test.Requirements.Append(machine.AsRequirement())
            : test.Requirements;

    /// <summary>
    /// Why a requirement the machine was asked about is unmet, said as a fact about the machine.
    /// </summary>
    /// <remarks>
    /// "This environment provides no egm.validation" is the wrong sentence once the question has
    /// actually been put to a machine: the environment was able to ask, and what happened is on
    /// the link or in the machine's answer.
    /// </remarks>
    private static string AskedReason(Requirement requirement, string reason, MachineAnswers answers) =>
        CapabilityProbe.IsAskedOfTheMachine(requirement.Key)
        && answers.WhyNot is { } why
        && reason.StartsWith("this environment provides no", StringComparison.Ordinal)
            ? why
            : reason;

    private TestOutcome Execute(
        ScriptTest test,
        ConnectStep connect,
        ScriptEnvironment environment,
        CapabilitySet capabilities,
        Stopwatch stopwatch,
        DateTimeOffset startedAt,
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
            return Skipped(test, stopwatch, startedAt, refused.Message, connect.Channel, testBase, provenance);
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
        var answered = new AnsweredPolls();
        var printed = new PrintedTickets(_options.WalletDirectory);

        //  Everything already in the wallet belongs to whatever ran before this test.
        printed.Baseline();

        using (engine)
        {
            try
            {
                engine.Start();
            }
            catch (TransportRefusedException refused)
            {
                return Skipped(test, stopwatch, startedAt, refused.Message, connect.Channel, testBase, provenance);
            }

            if (environment.HostTransport is TcpTransport tcp && !tcp.WaitForPeer(_options.PeerTimeout))
            {
                return Skipped(test, stopwatch, startedAt,
                    $"no gaming machine connected to {tcp.Description} within "
                    + $"{_options.PeerTimeout.TotalSeconds:F0}s",
                    connect.Channel, testBase, provenance);
            }

            // What only the machine can answer is asked here, now that there is a machine to ask
            // and before a single step has run. A case that needed one and did not get it is a
            // skip, exactly as it would have been had the answer come from this side of the link.
            List<Requirement> declared = Declared(test).ToList();
            string[] wanted = declared
                .Select(r => r.Key)
                .Where(CapabilityProbe.IsAskedOfTheMachine)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (wanted.Length > 0)
            {
                MachineAnswers answers = CapabilityProbe.FromMachine(engine, wanted);
                CapabilitySet asked = capabilities.With(answers.Values);
                if (asked.FirstUnmet(declared) is { } gap)
                {
                    return Skipped(test, stopwatch, startedAt,
                        $"{gap.Requirement.Key}: {AskedReason(gap.Requirement, gap.Reason, answers)}",
                        connect.Channel, testBase, provenance);
                }
            }

            RefreshBaselines(engine, test.WatchedMeters, baselines);

            foreach (ScriptStep step in test.Steps)
            {
                StepResult result = RunStep(step, engine, environment, exceptions, consumed,
                    test, baselines, window, answered, printed, stopwatch.Elapsed);
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
                Requirements = test.Requirements,
                Machine = test.Machine,
                StartedAt = startedAt,
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
        AnsweredPolls answered,
        PrintedTickets printed,
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

                SendLongPollStep send => Send(send, engine, answered, at),

                ForgetExceptionsStep forget => Forget(forget, engine, exceptions, consumed, at),

                StimulusStep stimulus => ApplyStimulus(
                    stimulus, engine, environment, test, baselines, window, answered, at),

                ExpectExceptionStep expect => CheckException(
                    expect, exceptions, consumed, window, at),

                ExpectMeterStep expect => CheckMeter(expect, engine, baselines, at),

                ExpectFieldStep expect => CheckField(expect, engine, answered, printed, at),

                ExpectEventFieldStep expect => CheckEventField(expect, exceptions, consumed, at),

                ExpectLinkStep expect => CheckLink(expect, engine, at),

                ExpectTicketStep expect => CheckTicketPrinted(expect, printed, at),

                ExpectTicketFieldStep expect => CheckTicketField(expect, printed, at),

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
    /// The replies this test has already been given, one per long poll.
    /// </summary>
    /// <remarks>
    /// A long poll carrying data is a question with an argument, and the answer belongs to that
    /// argument. Re-sending the command without it asks something else - a machine rejects a
    /// selected-meter poll with no meters in it - so an assertion made after a send reads what
    /// that send was told. Acting on the machine clears the lot, because a reply describes the
    /// machine at the moment it was given.
    /// </remarks>
    private sealed class AnsweredPolls
    {
        private readonly Dictionary<byte, LongPollResult> _replies = [];

        public void Remember(LongPollResult reply) => _replies[reply.Command] = reply;

        public LongPollResult? Recall(byte command) =>
            _replies.GetValueOrDefault(command);

        public void Forget() => _replies.Clear();
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

    private static StepResult Send(
        SendLongPollStep step, PollEngine engine, AnsweredPolls answered, TimeSpan at)
    {
        LongPollResult result = engine.Execute(step.Command, step.Data);
        Verdict verdict = result.Succeeded ? Verdict.Pass : Verdict.Fail;

        if (result.Succeeded)
            answered.Remember(result);

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
        AnsweredPolls answered,
        TimeSpan at)
    {
        // Take the baseline before acting, so a relative assertion afterwards measures the effect
        // of this step and not of everything since the test began.
        RefreshBaselines(engine, test.WatchedMeters, baselines);

        // Every reply captured so far describes the machine as it was before this step, so none of
        // them may answer an assertion made after it.
        answered.Forget();
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

    /// <summary>
    /// Waits for a real-time event and checks one field of what it carried.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three failures that look alike are kept apart, because they need different fixes. The event
    /// never arrived. It arrived as a bare exception code, which means the host never turned
    /// reporting on — a machine reporting the old way carries no payload at all, and saying "the
    /// field was missing" would send somebody looking at the layout. Or it arrived with bytes that
    /// do not fit the established payload, which is a finding about the machine.
    /// </para>
    /// <para>
    /// Unlike an exception expectation, a matched event is <em>not</em> consumed. The two steps ask
    /// different questions: one asks whether the event happened, so reading it twice would be
    /// reading two events, and this one asks what a payload held, so three assertions about three
    /// fields of one event have to see the same event. A script that means the next event says so
    /// with 'host forget exceptions' between the two, which is how it would separate two
    /// exceptions as well.
    /// </para>
    /// </remarks>
    private static StepResult CheckEventField(
        ExpectEventFieldStep step,
        List<ExceptionReport> exceptions,
        HashSet<long> consumed,
        TimeSpan at)
    {
        SasEventLayout? layout = SasEventLayouts.For(step.Code);
        if (layout is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: $"this build has established no payload for exception ${step.Code:X2}",
                At: at);
        }

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

        if (match is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(),
                Actual: $"no {step.EventName} in {step.Within.TotalMilliseconds:F0}ms",
                At: at) { IsAssertion = true };
        }

        if (match.Data is not { } payload)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: $"{step.EventName} arrived as a bare exception code, so it carried no "
                    + "payload: real-time event reporting is not on, and long poll $0E turns it on",
                At: at);
        }

        // Rebuilt into the message the decoder reads, so one reader covers a recorded event and a
        // live one and the two cannot drift.
        byte[] frame = new byte[SasEventLayouts.HeaderLength + payload.Length + 2];
        frame[1] = SasEventLayouts.Marker;
        frame[2] = step.Code;
        payload.CopyTo(frame, SasEventLayouts.HeaderLength);

        SasDecoded decoded = SasMessageDecoder.DecodeEvent(layout, frame);
        if (!decoded.Succeeded)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(), Actual: Convert.ToHexString(payload),
                Reason: decoded.Failure, At: at) { IsAssertion = true };
        }

        SasValue? value = decoded[step.Field];
        if (value is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(),
                Actual: $"the event held no '{step.Field}'; it held "
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
            actualText = number.ToString(CultureInfo.InvariantCulture);
            held = Compare(number, step.Comparison, step.ExpectedNumber!.Value);
        }
        else
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Expected: step.Describe(),
                Actual: $"'{step.Field}' is {value.ToHex()}, which is not a number",
                At: at) { IsAssertion = true };
        }

        return new StepResult(step.Location, step.Describe(), held ? Verdict.Pass : Verdict.Fail,
            Expected: step.Describe(), Actual: actualText, At: at) { IsAssertion = true };
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

    private static StepResult CheckTicketPrinted(
        ExpectTicketStep step, PrintedTickets printed, TimeSpan at)
    {
        PrintedTicket? ticket = printed.Take(step.Within, CancellationToken.None);
        if (ticket is not null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Pass,
                Reason: $"{ticket.FileName}: {ticket.Ticket.AmountCents} cents, "
                    + $"validation {ticket.Ticket.ValidationNumber}",
                At: at);
        }

        // A file that would not read is the more useful answer when there is one: it says the
        // machine did print and something about the ticket is wrong, which is a different problem
        // from a machine that printed nothing.
        string reason = printed.Problems.Count > 0
            ? $"no ticket was printed within {step.Within.TotalSeconds:0.##}s. The wallet holds "
                + $"{printed.Problems.Count} file(s) that would not read: "
                + string.Join("; ", printed.Problems)
            : $"no ticket was printed within {step.Within.TotalSeconds:0.##}s. Nothing new "
                + $"appeared in {printed.Directory}";

        return new StepResult(step.Location, step.Describe(), Verdict.Fail, Reason: reason, At: at);
    }

    private static StepResult CheckTicketField(
        ExpectTicketFieldStep step, PrintedTickets printed, TimeSpan at)
    {
        if (printed.Current is not { } ticket)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Error,
                Reason: "there is no printed ticket to assert on; expect one printed first",
                At: at);
        }

        TicketField field = TicketFields.Find(step.Field)!;
        string actual = field.Format(ticket.Ticket);

        bool held;
        if (step.ExpectedNumber is { } expected
            && long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            held = step.Comparison switch
            {
                SasComparison.Equal => value == expected,
                SasComparison.NotEqual => value != expected,
                SasComparison.AtLeast => value >= expected,
                SasComparison.AtMost => value <= expected,
                SasComparison.GreaterThan => value > expected,
                SasComparison.LessThan => value < expected,
                _ => false,
            };
        }
        else
        {
            string wanted = step.ExpectedText ?? step.ExpectedNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            bool same = string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase);
            held = step.Comparison == SasComparison.NotEqual ? !same : same;
        }

        string wantedText = step.ExpectedText
            ?? step.ExpectedNumber?.ToString(CultureInfo.InvariantCulture)
            ?? "?";

        return held
            ? new StepResult(step.Location, step.Describe(), Verdict.Pass,
                Reason: $"{ticket.FileName}: {step.Field} is {Shown(actual)}", At: at)
            : new StepResult(step.Location, step.Describe(), Verdict.Fail,
                Reason: $"{ticket.FileName}: {step.Field} is {Shown(actual)}, not {wantedText}",
                At: at);
    }

    /// <summary>An empty field said as a word, so a message does not trail off into nothing.</summary>
    private static string Shown(string value) => value.Length == 0 ? "unset" : value;

    private static StepResult CheckField(
        ExpectFieldStep step,
        PollEngine engine,
        AnsweredPolls answered,
        PrintedTickets printed,
        TimeSpan at)
    {
        //  Resolved before the poll is read, so a comparison against a ticket that was never
        //  printed says that rather than blaming the reply.
        long? fromTicket = null;
        if (step.ExpectedTicketField is not null)
        {
            if (printed.Current is not { } paper)
            {
                return new StepResult(step.Location, step.Describe(), Verdict.Error,
                    Reason: "there is no printed ticket to compare against; "
                        + "expect one printed first",
                    At: at);
            }

            string written = TicketFields.Find(step.ExpectedTicketField)!.Format(paper.Ticket);
            if (!long.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out long parsed))
            {
                return new StepResult(step.Location, step.Describe(), Verdict.Error,
                    Reason: $"{paper.FileName}: {step.ExpectedTicketField} is "
                        + $"{Shown(written)}, which is not a number",
                    At: at);
            }

            fromTicket = parsed;
        }

        SasMessageLayout? layout = SasLayouts.For(step.Command);
        if (layout is null)
        {
            return new StepResult(step.Location, step.Describe(), Verdict.Skip,
                Reason: $"this build cannot decode the reply to long poll ${step.Command:X2}", At: at);
        }

        // The reply this test already has, when it has one. Asking again would ask a different
        // question for any long poll that carries data, because the second ask carries none.
        LongPollResult reply = answered.Recall(step.Command) ?? engine.Execute(step.Command);
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

        if (fromTicket is { } wanted)
        {
            if (value.Number is not { } reported)
            {
                return new StepResult(step.Location, step.Describe(), Verdict.Fail,
                    Expected: step.Describe(),
                    Actual: $"'{step.Field}' is {value.ToHex()}, which is not a number",
                    At: at) { IsAssertion = true };
            }

            actualText = reported.ToString(CultureInfo.InvariantCulture);
            held = Compare(reported, step.Comparison, wanted);
        }
        else if (step.ExpectedText is not null)
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
            Expected: fromTicket?.ToString(CultureInfo.InvariantCulture)
                ?? step.ExpectedText ?? step.ExpectedNumber?.ToString(),
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

            (long? value, _) = ReadMeter(engine, source, WirePurpose.MeterBaseline);
            if (value is not null)
                baselines[meter] = value.Value;
        }
    }

    /// <param name="purpose">
    /// Why the meter is being read, for the reads no step asked for. A baseline is taken before
    /// the first step and again before every stimulus, so a log that did not mark them would show
    /// polls a reader could not account for.
    /// </param>
    private static (long? Value, string? Failure) ReadMeter(
        PollEngine engine, MeterSource source, string? purpose = null)
    {
        SasMessageLayout? layout = SasLayouts.For(source.Command);
        if (layout is null)
            return (null, $"long poll ${source.Command:X2} has no layout");

        LongPollResult reply = engine.Execute(source.Command, purpose: purpose);
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
        DateTimeOffset startedAt,
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
            Requirements = test.Requirements,
            Machine = test.Machine,
            StartedAt = startedAt,
            Duration = stopwatch.Elapsed,
            SkipReason = reason,
            Provenance = provenance,
            Steps = [new StepResult(test.Location, test.Name, Verdict.Skip, Reason: reason)],
        };

        Log.TestFinished(outcome, channel);
        return outcome;
    }
}
