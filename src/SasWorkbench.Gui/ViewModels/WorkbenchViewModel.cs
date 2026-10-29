using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Recording;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;
using SasWorkbench.Qpm;
using SasWorkbench.Gui.Controls;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>
/// The window's state. Everything it does goes through the same session, layouts and decoder a
/// script run uses, so what a person can do by hand is exactly what a test can express.
/// </summary>
public sealed partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly SessionLog _log = new();
    private HostSession? _session;
    private CancellationTokenSource? _run;
    private IStimulusBackend _stimulus = new NoStimulusBackend();
    private FakeEgm? _machine;
    private IReadOnlyList<RecordedAction> _link = [];

    public WorkbenchViewModel()
    {
        Transports = ["loopback", "tcp", "serial"];
        SelectedTransport = "loopback";
        SerialPorts = SerialTransport.AvailablePorts();
        LongPolls = SasCatalogue.LongPolls.Values.OrderBy(l => l.Code).ToList();
        SelectedLongPoll = LongPolls.FirstOrDefault(l => l.Code == 0x1F) ?? LongPolls[0];
        SelectedInstrument = Instruments[0];
        Channels = ChannelOption.Offer(CapabilityProbe.Baseline());
        SelectedChannel = Channels[0];
        Recorder = new SessionRecorder(_log);
        Stimulus.Selected = Stimulus.Actions[0];

        // One log for the window, not one per link. A run closes the interactive session and opens
        // its own, and the stream has to survive that: what is durable is the record, not the link.
        Stream.Watch(_log);

        Suite.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SuiteViewModel.Selected))
                return;

            ScopeToSelectedTest();
            OnPropertyChanged(nameof(HasSuiteDetail));
        };

        Rediscover();
    }

    /// <summary>The spine: everything this session did, in order.</summary>
    public SessionStreamViewModel Stream { get; } = new();

    /// <summary>The suite: what tests exist and what each of them came to.</summary>
    public SuiteViewModel Suite { get; } = new();

    /// <summary>The band that asks a person to act on the machine during a manual run.</summary>
    public ManualPromptViewModel Prompt { get; } = new();

    /// <summary>What a person can do to the machine by hand.</summary>
    public StimulusViewModel Stimulus { get; } = new();

    /// <summary>Watches what a person does, so it can be written out as a script.</summary>
    public SessionRecorder Recorder { get; }

    // ---- connection ----------------------------------------------------------------------

    public IReadOnlyList<string> Transports { get; }

    public IReadOnlyList<string> SerialPorts { get; }

    [ObservableProperty]
    public partial string SelectedTransport { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; } = $"127.0.0.1:{TcpTransportOptions.DefaultPort}";

    [ObservableProperty]
    public partial string Address { get; set; } = "01";

    [ObservableProperty]
    public partial ChannelOption SelectedChannel { get; set; } = null!;

    [ObservableProperty]
    public partial string PollInterval { get; set; } = "200ms";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string LinkState { get; set; } = "down";

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>Whether the lamp is filled.</summary>
    [ObservableProperty]
    public partial bool IsLinkUp { get; set; }

    // ---- provenance ----------------------------------------------------------------------

    /// <summary>Whether a pass on this link is evidence, or only a claim.</summary>
    [ObservableProperty]
    public partial bool IsLinkProven { get; set; }

    /// <summary>Whether an open link is only a claim; with no link there is nothing to qualify.</summary>
    public bool IsLinkUnverified => IsConnected && !IsLinkProven;

    partial void OnIsLinkProvenChanged(bool value) => OnPropertyChanged(nameof(IsLinkUnverified));

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsLinkUnverified));

    /// <summary>The link and what is driving it, in one line.</summary>
    [ObservableProperty]
    public partial string LinkClaim { get; set; } = "no link";

    /// <summary>The sentence a reader needs in order not to over-read a result.</summary>
    [ObservableProperty]
    public partial string? LinkCaveat { get; set; }

    /// <summary>Whether there is a caveat to show at all.</summary>
    public bool HasLinkCaveat => !string.IsNullOrWhiteSpace(LinkCaveat);

    partial void OnLinkCaveatChanged(string? value) => OnPropertyChanged(nameof(HasLinkCaveat));

    [ObservableProperty]
    public partial string Counters { get; set; } = string.Empty;

    /// <summary>How full the log is, so a reader knows when it has started overwriting.</summary>
    [ObservableProperty]
    public partial string LogBound { get; set; } = "log 0/0";

    /// <summary>
    /// The four channels, with the three this build has never opened marked as such rather than
    /// offered as ordinary choices.
    /// </summary>
    public IReadOnlyList<ChannelOption> Channels { get; }

    // ---- instruments ---------------------------------------------------------------------

    /// <summary>
    /// The rail. Instruments a later milestone brings are listed but not offered, because a
    /// surface that is coming and a surface that is missing are different things to a reader.
    /// </summary>
    public IReadOnlyList<RailItem> Instruments { get; } =
    [
        new RailItem("POLL", 0),
        new RailItem("METERS", 1),
        new RailItem("EXCEPTIONS", 2),
        new RailItem("SUITE", 3),
        new RailItem("STIMULUS", 4),
        new RailItem("RECORD", 5),
        new RailItem("CATALOGUE", -1, "M4"),
        new RailItem("SESSION", -1, "M4"),
        new RailItem("TICKETS", -1, "M3"),
        new RailItem("TRACES", -1, "M4"),
    ];

    [ObservableProperty]
    public partial RailItem? SelectedInstrument { get; set; }

    /// <summary>Which instrument body is showing.</summary>
    [ObservableProperty]
    public partial int InstrumentIndex { get; set; }

    partial void OnSelectedInstrumentChanged(RailItem? value)
    {
        if (value is { Index: >= 0 })
            InstrumentIndex = value.Index;
    }

    // ---- live views ----------------------------------------------------------------------

    public ObservableCollection<ExceptionLine> Exceptions { get; } = [];

    public ObservableCollection<MeterLine> Meters { get; } = [];

    public ObservableCollection<FieldLine> Fields { get; } = [];

    [ObservableProperty]
    public partial string? LastReply { get; set; }

    // ---- long poll -----------------------------------------------------------------------

    public IReadOnlyList<SasLongPollInfo> LongPolls { get; }

    [ObservableProperty]
    public partial SasLongPollInfo SelectedLongPoll { get; set; }

    [ObservableProperty]
    public partial string LongPollData { get; set; } = string.Empty;

    // ---- script --------------------------------------------------------------------------

    [ObservableProperty]
    public partial string ScriptPath { get; set; } = "tests";

    partial void OnScriptPathChanged(string value) => Rediscover();

    /// <summary>
    /// Rebuilds the tree from disk. Parsing only: the corpus is worth seeing before anything is
    /// connected, and a script that cannot be parsed should say so here rather than at run time.
    /// </summary>
    private void Rediscover()
    {
        try
        {
            Suite.Discover(Resolve(ScriptPath));
        }
        catch (Exception ex) when (ex is ScriptParseException or FileNotFoundException
                                       or DirectoryNotFoundException)
        {
            Suite.Nodes.Clear();
            Status = ex.Message;
        }
    }

    /// <summary>
    /// Narrows the spine to the selected test's own window, so the frames a verdict was reached on
    /// are the ones on screen.
    /// </summary>
    private void ScopeToSelectedTest()
    {
        if (Suite.Selected is not { Kind: SuiteNodeKind.Test, Outcome: not null } test)
        {
            Stream.ClearScope();
            return;
        }

        if (_log.WindowOf(test.Name) is { } window)
            Stream.ScopeTo(window.From, window.To, test.Name);
        else
            Stream.ClearScope();
    }

    [ObservableProperty]
    public partial string? RunSummary { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The last run, kept so a report can be written from it.</summary>
    public RunReport? LastReport { get; private set; }

    /// <summary>Which reports to write. The self-contained page is the one worth sending.</summary>
    [ObservableProperty]
    public partial string ReportFormats { get; set; } = "html";

    /// <summary>
    /// The last run, explained packet by packet, or the reason there is nothing to explain.
    /// </summary>
    /// <remarks>
    /// Rendered on demand rather than as a run goes, because it is a rendering of the record and
    /// the record is complete only once the run is. The same renderer the command line uses, so
    /// what is read here and what is read in a file are the same text.
    /// </remarks>
    [ObservableProperty]
    public partial string? ExtendedLog { get; set; }

    /// <summary>
    /// Whether there is anything to show below the tree: a test to inspect, or a run explained.
    /// </summary>
    /// <remarks>
    /// The extended log is about the whole run rather than about one case, so it must not be
    /// reachable only through selecting a case. Selecting one still fills the other two tabs.
    /// </remarks>
    public bool HasSuiteDetail => Suite.HasSelection || !string.IsNullOrEmpty(ExtendedLog);

    partial void OnExtendedLogChanged(string? value) => OnPropertyChanged(nameof(HasSuiteDetail));

    /// <summary>What should drive the machine during a run.</summary>
    public IReadOnlyList<StimulusBackend> StimulusBackends { get; } =
        Enum.GetValues<StimulusBackend>();

    [ObservableProperty]
    public partial StimulusBackend RunStimulus { get; set; } = StimulusBackend.Software;

    /// <summary>
    /// Asks the person at the window, and records what they said in the stream, so a run that was
    /// helped along by a person says so in its own log.
    /// </summary>
    private PromptAnswer AskAtTheWindow(string request, TimeSpan deadline)
    {
        PromptAnswer answer = Prompt.Ask(request, deadline);
        _log.Prompt(request, SasChannel.One, answer.ToString().ToLowerInvariant());
        return answer;
    }

    /// <summary>Whether the deadline field is worth showing at all.</summary>
    public bool IsManualStimulus => RunStimulus == StimulusBackend.Manual;

    /// <summary>Whether the machine's own resource directory is worth asking for.</summary>
    public bool IsPeripheralStimulus => RunStimulus == StimulusBackend.Qpm;

    /// <summary>
    /// Where the machine under test declares its doors and buttons.
    /// </summary>
    /// <remarks>
    /// Without it the peripheral driver knows the shared region but not which bit is which door,
    /// so it offers no door and no button at all and the tests needing one skip by name. That is
    /// the honest outcome: an index guessed from the peripheral simulator's labels would open a
    /// different door from the one the test asked for.
    /// </remarks>
    [ObservableProperty]
    public partial string EgmResources { get; set; } = string.Empty;

    /// <summary>
    /// Which link a run should use, or "as written" to leave each test's own connect step alone.
    /// </summary>
    public IReadOnlyList<string> RunLinks { get; } = ["as written", "loopback", "tcp", "serial"];

    [ObservableProperty]
    public partial string RunLink { get; set; } = "as written";

    partial void OnRunStimulusChanged(StimulusBackend value)
    {
        OnPropertyChanged(nameof(IsManualStimulus));
        OnPropertyChanged(nameof(IsPeripheralStimulus));
    }

    // ---- commands ------------------------------------------------------------------------

    [RelayCommand]
    private void Connect()
    {
        Disconnect();

        if (!SasAddress.TryParse(Address, out SasAddress address))
        {
            Status = $"'{Address}' is not a poll address. Expected 00 to 7F.";
            return;
        }

        if (!Enum.TryParse(SelectedTransport, ignoreCase: true, out ScriptTransport transport))
        {
            Status = $"'{SelectedTransport}' is not a link.";
            return;
        }

        TimeSpan interval;
        try
        {
            interval = ScriptParser.ParseDuration(PollInterval, SourceLocation.None);
        }
        catch (ScriptParseException parse)
        {
            Status = parse.Reason;
            return;
        }

        if (!SelectedChannel.IsAvailable)
        {
            Status = $"channel {SelectedChannel.Number}: {SelectedChannel.Reason}";
            return;
        }

        var spec = new HostSessionSpec(
            transport, Target, address, (SasChannel)SelectedChannel.Number, interval);

        //  The machine the interactive session talks to is captured on the way past, because a
        //  person driving it by hand needs something to drive: the same backend a script run uses,
        //  so what a person can do here is exactly what a test can express.
        FakeEgm? started = null;
        try
        {
            _session = HostSession.Open(
                spec, SoftwareMachinePeer.For(started: machine => started = machine), _log);
        }
        catch (TransportRefusedException refused)
        {
            Status = refused.Message;
            return;
        }

        _machine = started;
        _stimulus = BuildStimulus(started);

        _session.ExceptionReported += OnException;
        _session.LinkStateChanged += OnLinkState;

        IsConnected = true;
        LinkState = _session.State.ToString().ToLowerInvariant();
        IsLinkUp = _session.State == Core.Polling.LinkState.Up;
        Status = $"connected to {_session.Description}";

        _link = Preamble(transport, address, interval);
        foreach (RecordedAction action in _link)
            Recorder.Did(action.What, action.Step);

        TransportCapabilities caps = _session.Capabilities;
        IsLinkProven = caps.Fidelity == TransportFidelity.Proven;
        LinkClaim = $"{_session.Description} · stimulus "
                    + _stimulus.Kind.ToString().ToLowerInvariant();
        LinkCaveat = caps.Caveat;

        RefreshCounters();
    }

    [RelayCommand]
    private void Disconnect()
    {
        HostSession? session = _session;
        _session = null;
        if (session is null)
            return;

        session.ExceptionReported -= OnException;
        session.LinkStateChanged -= OnLinkState;
        session.Dispose();

        _machine = null;
        _stimulus = new NoStimulusBackend();
        _link = [];

        IsConnected = false;
        LinkState = "down";
        IsLinkUp = false;
        LinkClaim = "no link";
        LinkCaveat = null;
        IsLinkProven = false;
        Status = "disconnected";
    }

    [RelayCommand]
    private async Task SendLongPollAsync()
    {
        HostSession? session = _session;
        if (session is null)
        {
            Status = "connect first";
            return;
        }

        byte[] data;
        try
        {
            string cleaned = LongPollData.Replace(" ", string.Empty).Replace("-", string.Empty);
            data = cleaned.Length == 0 ? [] : Convert.FromHexString(cleaned);
        }
        catch (FormatException)
        {
            Status = $"'{LongPollData}' is not hex data.";
            return;
        }

        byte command = SelectedLongPoll.Code;
        IsBusy = true;
        try
        {
            Recorder.Did(
                data.Length == 0
                    ? $"send long poll ${command:X2}"
                    : $"send long poll ${command:X2} with {Convert.ToHexString(data)}",
                new SendLongPollStep(SourceLocation.None, command, data));

            (LongPollResult result, SasDecoded? decoded) = await Task.Run(
                () => session.SendAndDecode(command, data));

            LastReply = result.ToString();
            Fields.Clear();

            if (decoded is null)
            {
                // Said plainly rather than left blank: a reply this build cannot decode is a gap
                // in the model, and a test that needed it would be skipped for the same reason.
                Fields.Add(new FieldLine(
                    SasLayouts.IsKnownButUndecoded(command) ? "not decoded" : "no reply",
                    SasLayouts.IsKnownButUndecoded(command)
                        ? $"this build has no layout for the reply to ${command:X2}"
                        : result.Detail ?? result.Outcome.ToString(),
                    string.Empty));
            }
            else if (!decoded.Succeeded)
            {
                Fields.Add(new FieldLine("could not decode", decoded.Failure ?? "unknown", string.Empty));
            }
            else
            {
                foreach (SasValue value in decoded.Values)
                {
                    Fields.Add(new FieldLine(
                        value.Path,
                        value.Text ?? value.Number?.ToString() ?? value.ToHex(),
                        value.Violation ?? (value.Field.Unit == SasUnit.None
                            ? string.Empty
                            : value.Field.Unit.ToString())));
                }
            }

            RefreshCounters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshMetersAsync()
    {
        HostSession? session = _session;
        if (session is null)
        {
            Status = "connect first";
            return;
        }

        IsBusy = true;
        try
        {
            //  Recorded as something that happened and not as a step: reading forty meters is an
            //  observation, and every number it produces is the machine's condition rather than
            //  anything a person chose. A script that asserted them would fail on its next run.
            Recorder.Did("read every meter this build can read by name");

            IReadOnlyList<MeterReading> readings = await Task.Run(() => session.ReadMeters());

            Meters.Clear();
            foreach (MeterReading reading in readings)
            {
                Meters.Add(new MeterLine(
                    reading.Name,
                    $"${reading.Command:X2}",
                    reading.Value?.ToString() ?? "—",
                    reading.Problem ?? (reading.Unit == SasUnit.None
                        ? string.Empty
                        : reading.Unit.ToString().ToLowerInvariant())));
            }

            RefreshCounters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        // A script run opens its own link, so the interactive session is closed first rather than
        // having two hosts polling one machine and each seeing half the exceptions.
        bool wasConnected = IsConnected;
        Disconnect();

        IsBusy = true;
        RunSummary = null;

        try
        {
            string resolved = Resolve(ScriptPath);
            _run = new CancellationTokenSource();
            CancellationToken cancel = _run.Token;
            StimulusBackend stimulus = RunStimulus;
            string? resources = string.IsNullOrWhiteSpace(EgmResources) ? null : EgmResources;
            TimeSpan promptDeadline = ScriptParser.ParseDuration(
                Prompt.Deadline, SourceLocation.None);

            if (!TryOverride(out ConnectionOverride change, out string? wrong))
            {
                Status = wrong;
                return;
            }

            RunReport report = await Task.Run(() =>
            {
                IReadOnlyList<ScriptDocument> documents = change.ApplyTo(ScriptLoader.Load(resolved));
                var runner = new ScriptRunner(new ScriptRunnerOptions
                {
                    Stimulus = stimulus,
                    AskPerson = stimulus == StimulusBackend.Manual ? AskAtTheWindow : null,
                    PromptDeadline = promptDeadline,

                    // The software machine is wired in only when it was asked for: a run that
                    // quietly substituted a fixture for the machine under test would be worthless.
                    Environment = stimulus switch
                    {
                        StimulusBackend.Software => SoftwareMachineEnvironment.For(),
                        StimulusBackend.Qpm => QpmEnvironment.For(
                            new QpmOptions(ResourcesDirectory: resources)),
                        _ => DefaultEnvironment.Create,
                    },
                    Log = _log,
                });

                return runner.RunAll(documents, cancel);
            }, cancel);

            Suite.Apply(report);
            RunSummary = $"{report.Verdict.ToString().ToUpperInvariant()} — {report.Summary()}";
            LastReport = report;
            Status = string.Join("; ", report.Provenance.Select(p => p.ToString()));
        }
        catch (OperationCanceledException)
        {
            // A cancelled run has no verdict, and saying so beats leaving the last one on screen
            // as though it still described the code.
            RunSummary = null;
            Status = "run cancelled; the tests that had already finished are in the stream";
        }
        catch (Exception ex) when (ex is ScriptParseException or FileNotFoundException
                                      or DirectoryNotFoundException)
        {
            RunSummary = null;
            Status = ex.Message;
        }
        finally
        {
            _run?.Dispose();
            _run = null;
            IsBusy = false;
            if (wasConnected)
                Connect();
        }
    }

    /// <summary>
    /// Finds a script path the way a person expects, rather than the way a process does.
    /// </summary>
    /// <remarks>
    /// A relative path typed into the window is relative to the repository, not to whichever
    /// directory the executable happens to sit in. Without this, the default path resolves against
    /// the output folder, finds nothing, and the window reports a missing directory for a suite
    /// that is plainly there.
    /// </remarks>
    private static string Resolve(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path) || Directory.Exists(path))
            return path;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return path;
    }

    /// <summary>
    /// Turns the link selector into an override, so one suite can be pointed at a different
    /// machine without editing it. The same override the command line applies.
    /// </summary>
    private bool TryOverride(out ConnectionOverride change, out string? error)
    {
        change = ConnectionOverride.None;
        error = null;

        if (RunLink == "as written")
            return true;

        if (!Enum.TryParse(RunLink, ignoreCase: true, out ScriptTransport transport))
        {
            error = $"'{RunLink}' is not a link.";
            return false;
        }

        change = new ConnectionOverride(transport, Target);
        return change.TryValidate(out error);
    }

    /// <summary>Stops a run in progress. What already ran keeps its verdicts.</summary>
    [RelayCommand]
    private void CancelRun()
    {
        _run?.Cancel();
        Status = "cancelling…";
    }

    /// <summary>
    /// Writes the last run to disk, so a result can be sent to somebody or kept as a record.
    /// </summary>
    [RelayCommand]
    private void SaveReport()
    {
        if (LastReport is not { } report)
        {
            Status = "run something first";
            return;
        }

        if (!Reports.TryParse(ReportFormats, out ReportFormat[] formats, out string? unknown))
        {
            Status = $"'{unknown}' is not a report format. Known: {Reports.Names}.";
            return;
        }

        string directory = Path.Combine(
            Resolve("runs"), report.StartedAt.ToLocalTime().ToString("yyyy-MM-dd-HHmmss"));

        try
        {
            IReadOnlyList<string> written = Reports.Write(report, directory, formats);
            Status = $"wrote {string.Join(", ", written)}";
        }
        catch (IOException io)
        {
            Status = $"could not write the report: {io.Message}";
        }
        catch (UnauthorizedAccessException denied)
        {
            Status = $"could not write the report: {denied.Message}";
        }
    }

    /// <summary>
    /// Explains the last run: what each case was, and what every packet meant.
    /// </summary>
    /// <remarks>
    /// A packet whose layout this build has not established is named and marked undecoded rather
    /// than guessed at, so the tally at the end of the log says how much of what crossed the wire
    /// this build actually understands.
    /// </remarks>
    [RelayCommand]
    private void ExplainRun()
    {
        if (LastReport is not { } report)
        {
            ExtendedLog = null;
            Status = "run something first";
            return;
        }

        ExtendedLog = new ExtendedLogWriter().Render(report);
        Status = "explained the last run; a packet marked 'known but not decoded' is a layout "
                 + "this build has not established";
    }

    /// <summary>
    /// Does to the machine what the person asked for, and records what came of it.
    /// </summary>
    /// <remarks>
    /// A refusal is recorded as an action that did not happen, with the backend's own reason, and
    /// no step is written for it. The alternative — recording the intent as though it had worked —
    /// is the one thing a recorder must never do: the script would then drive a machine nobody
    /// drove, and everything asserted after it would be about a machine in another state.
    /// </remarks>
    [RelayCommand]
    private void ApplyStimulus()
    {
        if (Stimulus.Selected is not { } action)
        {
            Stimulus.Outcome = "choose something first";
            return;
        }

        if (!IsConnected)
        {
            Stimulus.Outcome = "connect first: a machine nobody is polling reports nothing";
            return;
        }

        try
        {
            _stimulus.Apply(action.Step);
            Stimulus.Outcome = $"{action.Label}: done";
            Recorder.Did(action.Label, action.Step);
        }
        catch (StimulusUnavailableException unavailable)
        {
            Stimulus.Outcome = $"{action.Label}: {unavailable.Reason}";
            Recorder.Did(action.Label, action.Step, unavailable.Reason);
        }
    }

    // ---- recording -----------------------------------------------------------------------

    /// <summary>Whether a recording is running, for the button and the lamp.</summary>
    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    /// <summary>What this recording has been called, which becomes the test's name.</summary>
    [ObservableProperty]
    public partial string RecordingName { get; set; } = "what I did by hand";

    /// <summary>The script the last recording produced, or null before there is one.</summary>
    [ObservableProperty]
    public partial string? RecordedScript { get; set; }

    /// <summary>What the last recording could not capture, one line each.</summary>
    public ObservableCollection<CaveatLine> Caveats { get; } = [];

    [RelayCommand]
    private void StartRecording()
    {
        Caveats.Clear();
        RecordedScript = null;

        //  The link goes in as the recording's preamble rather than being waited for. A person
        //  connects, watches the machine for a while and only then presses Record, and a script
        //  with no link in it cannot run at all.
        Recorder.Start(_link);
        IsRecording = true;
        Status = "recording; everything done from here on is written down, and everything that "
                 + "cannot be is said plainly";
    }

    /// <summary>
    /// Stops recording and writes the script out.
    /// </summary>
    /// <remarks>
    /// The caveats are shown beside the script rather than only inside it, because the question a
    /// person has at this moment is "does this do what I just did" — and the answer is entirely in
    /// the list of things it left out.
    /// </remarks>
    [RelayCommand]
    private void StopRecording()
    {
        if (!IsRecording)
        {
            Status = "nothing is being recorded";
            return;
        }

        Recorded recorded = Recorder.Stop();
        IsRecording = false;

        if (!recorded.AnythingHappened)
        {
            RecordedScript = null;
            Status = "nothing was done while that was recording, so there is no script";
            return;
        }

        EmittedScript emitted = ScriptEmitter.Emit(
            recorded,
            string.IsNullOrWhiteSpace(RecordingName) ? "what I did by hand" : RecordingName);

        RecordedScript = emitted.Script;
        foreach (RecordingCaveat caveat in emitted.Caveats)
            Caveats.Add(new CaveatLine(RecordingCaveat.Describe(caveat.Kind), caveat.What, caveat.Why));

        Status = $"recorded {emitted}";
    }

    /// <summary>Writes the recorded script where a run can find it.</summary>
    [RelayCommand]
    private void SaveRecording()
    {
        if (RecordedScript is not { } script)
        {
            Status = "record something first";
            return;
        }

        string directory = Resolve("runs");
        string file = Path.Combine(
            directory,
            $"recorded-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}.sas");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(file, script);
            Status = $"wrote {file}";
        }
        catch (IOException io)
        {
            Status = $"could not write the script: {io.Message}";
        }
        catch (UnauthorizedAccessException denied)
        {
            Status = $"could not write the script: {denied.Message}";
        }
    }

    /// <summary>
    /// The link a recording is made over, as the steps that would open it again.
    /// </summary>
    private static IReadOnlyList<RecordedAction> Preamble(
        ScriptTransport transport, SasAddress address, TimeSpan interval) =>
    [
        new(TimeSpan.Zero, SasChannel.One,
            $"connect {transport.ToString().ToLowerInvariant()} address {address}",
            new ConnectStep(
                SourceLocation.None, transport,
                transport == ScriptTransport.Loopback ? string.Empty : string.Empty,
                address, SasChannel.One)),
        new(TimeSpan.Zero, SasChannel.One, $"poll every {interval.TotalMilliseconds:F0}ms",
            new PollEveryStep(SourceLocation.None, interval)),
    ];

    /// <summary>
    /// The backend a person drives the machine with by hand.
    /// </summary>
    /// <remarks>
    /// The same backends a run uses, so a hand-driven door and a scripted one are the same door.
    /// One that cannot be built comes back as a backend that refuses with the reason rather than
    /// as one that does nothing, because a stimulus that silently did nothing is what makes an
    /// assertion afterwards pass for the wrong reason.
    /// </remarks>
    private IStimulusBackend BuildStimulus(FakeEgm? machine) => RunStimulus switch
    {
        StimulusBackend.Software when machine is not null =>
            new SoftwareMachineStimulus(machine, Resolve("runs")),

        StimulusBackend.Software => new RefusingStimulusBackend(
            "the software gaming machine answers a loopback link, and this session is on another"),

        StimulusBackend.Qpm => QpmEnvironment.Attach(
            new QpmOptions(ResourcesDirectory: string.IsNullOrWhiteSpace(EgmResources)
                ? null
                : EgmResources)),

        StimulusBackend.Manual => new ManualStimulusBackend(
            AskAtTheWindow, ScriptParser.ParseDuration(Prompt.Deadline, SourceLocation.None)),

        _ => new NoStimulusBackend(),
    };

    [RelayCommand]
    private void ClearLog()
    {
        Stream.Clear();
        Exceptions.Clear();
    }

    public void Dispose()
    {
        Prompt.Dispose();
        Disconnect();
        Stream.Dispose();
    }

    // ---- plumbing ------------------------------------------------------------------------

    private void OnException(ExceptionReport report) => Dispatcher.UIThread.Post(() =>
    {
        Exceptions.Insert(0, new ExceptionLine(
            $"${report.Code:X2}",
            SasNames.ExceptionName(report.Code),
            SasCatalogue.Exceptions.TryGetValue(report.Code, out SasExceptionInfo? info)
                ? info.Description
                : "not in the catalogue",
            $"{report.Elapsed.TotalMilliseconds:F0} ms"));

        RefreshCounters();
    });

    private void OnLinkState(LinkState state) => Dispatcher.UIThread.Post(() =>
    {
        LinkState = state.ToString().ToLowerInvariant();
        IsLinkUp = state == Core.Polling.LinkState.Up;
        RefreshCounters();
    });

    private void RefreshCounters()
    {
        PollStatistics stats = _session?.Statistics ?? PollStatistics.Empty;
        Counters = $"{stats.SyncPolls} sync · {stats.GeneralPolls} general · {stats.LongPolls} long · "
                   + $"{stats.Exceptions} exceptions · {stats.Silences} silences";
        LogBound = $"log {_log.Recorded}/{_log.Capacity}";
    }
}

/// <summary>One instrument on the rail.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Index">Which body it shows, or -1 when a later milestone brings it.</param>
/// <param name="Milestone">The milestone that brings it, when it is not here yet.</param>
public sealed record RailItem(string Name, int Index, string? Milestone = null)
{
    /// <summary>Whether this instrument can be selected at all.</summary>
    public bool IsAvailable => Index >= 0;

    /// <summary>What a screen reader is told about an instrument that is not here yet.</summary>
    public string Automation => IsAvailable
        ? Name
        : $"{Name} — not in this build; {Milestone} brings it";
}

/// <summary>One exception the machine reported.</summary>
public sealed record ExceptionLine(string Code, string Name, string Meaning, string At);

/// <summary>One meter, as read.</summary>
public sealed record MeterLine(string Name, string From, string Value, string Note);

/// <summary>One field of the last reply.</summary>
public sealed record FieldLine(string Name, string Value, string Note);

/// <summary>One thing a recording could not capture.</summary>
/// <param name="Kind">Which of the five it is.</param>
/// <param name="What">What was done, or which value it was about.</param>
/// <param name="Why">Why it cannot be written down.</param>
public sealed record CaveatLine(string Kind, string What, string Why);

/// <summary>One line of a script run's result.</summary>
public sealed record StepLine(ChipStatus Verdict, string What, string Where, string Detail);
