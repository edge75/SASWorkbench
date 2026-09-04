using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Reporting;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Transport;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;

namespace SasWorkbench.Gui.ViewModels;

/// <summary>
/// The window's state. Everything it does goes through the same session, layouts and decoder a
/// script run uses, so what a person can do by hand is exactly what a test can express.
/// </summary>
public sealed partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How many messages the log keeps. A busy link produces five a second, so an unbounded list
    /// would grow without limit while somebody watched it over lunch.
    /// </summary>
    private const int WireLimit = 2000;

    private HostSession? _session;

    public WorkbenchViewModel()
    {
        Transports = ["loopback", "tcp", "serial"];
        SelectedTransport = "loopback";
        SerialPorts = SerialTransport.AvailablePorts();
        LongPolls = SasCatalogue.LongPolls.Values.OrderBy(l => l.Code).ToList();
        SelectedLongPoll = LongPolls.FirstOrDefault(l => l.Code == 0x1F) ?? LongPolls[0];
    }

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
    public partial int SelectedChannel { get; set; } = 1;

    [ObservableProperty]
    public partial string PollInterval { get; set; } = "200ms";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string LinkState { get; set; } = "down";

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>What this link is worth, said plainly next to the results.</summary>
    [ObservableProperty]
    public partial string? Provenance { get; set; }

    [ObservableProperty]
    public partial string Counters { get; set; } = string.Empty;

    public IReadOnlyList<int> Channels { get; } = [1, 2, 3, 4];

    // ---- live views ----------------------------------------------------------------------

    public ObservableCollection<WireLine> Wire { get; } = [];

    public ObservableCollection<ExceptionLine> Exceptions { get; } = [];

    public ObservableCollection<MeterLine> Meters { get; } = [];

    public ObservableCollection<FieldLine> Fields { get; } = [];

    public ObservableCollection<StepLine> Steps { get; } = [];

    [ObservableProperty]
    public partial string? LastReply { get; set; }

    [ObservableProperty]
    public partial bool FollowWire { get; set; } = true;

    // ---- long poll -----------------------------------------------------------------------

    public IReadOnlyList<SasLongPollInfo> LongPolls { get; }

    [ObservableProperty]
    public partial SasLongPollInfo SelectedLongPoll { get; set; }

    [ObservableProperty]
    public partial string LongPollData { get; set; } = string.Empty;

    // ---- script --------------------------------------------------------------------------

    [ObservableProperty]
    public partial string ScriptPath { get; set; } = Path.Combine("tests", "regression");

    [ObservableProperty]
    public partial string? RunSummary { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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

        var spec = new HostSessionSpec(
            transport, Target, address, (SasChannel)SelectedChannel, interval);

        try
        {
            _session = HostSession.Open(spec, SoftwareMachinePeer.For());
        }
        catch (TransportRefusedException refused)
        {
            Status = refused.Message;
            return;
        }

        _session.Wire += OnWire;
        _session.ExceptionReported += OnException;
        _session.LinkStateChanged += OnLinkState;

        IsConnected = true;
        LinkState = _session.State.ToString().ToLowerInvariant();
        Status = $"connected to {_session.Description}";

        TransportCapabilities caps = _session.Capabilities;
        Provenance = caps.Caveat is null
            ? $"{_session.Description} · {caps.Fidelity.ToString().ToLowerInvariant()}"
            : $"{_session.Description} · {caps.Fidelity.ToString().ToLowerInvariant()} · {caps.Caveat}";

        RefreshCounters();
    }

    [RelayCommand]
    private void Disconnect()
    {
        HostSession? session = _session;
        _session = null;
        if (session is null)
            return;

        session.Wire -= OnWire;
        session.ExceptionReported -= OnException;
        session.LinkStateChanged -= OnLinkState;
        session.Dispose();

        IsConnected = false;
        LinkState = "down";
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
        Steps.Clear();
        RunSummary = null;

        try
        {
            string resolved = Resolve(ScriptPath);
            RunReport report = await Task.Run(() =>
            {
                IReadOnlyList<ScriptDocument> documents = ScriptLoader.Load(resolved);
                var runner = new ScriptRunner(new ScriptRunnerOptions
                {
                    Stimulus = StimulusBackend.Software,
                    Environment = SoftwareMachineEnvironment.For(),
                });

                return runner.RunAll(documents);
            });

            foreach (TestOutcome test in report.Tests)
            {
                Steps.Add(new StepLine(
                    test.Verdict.ToString().ToUpperInvariant(),
                    test.Name,
                    test.Location.ToString(),
                    test.SkipReason ?? $"{test.Assertions} assert(s), {test.Duration.TotalSeconds:F1}s"));

                foreach (StepResult step in test.Steps.Where(s => s.Verdict != Verdict.Pass))
                {
                    Steps.Add(new StepLine(
                        step.Verdict.ToString().ToUpperInvariant(),
                        "    " + step.Description,
                        step.Location.ToString(),
                        step.Reason ?? $"expected {step.Expected}, got {step.Actual}"));
                }
            }

            RunSummary = $"{report.Verdict.ToString().ToUpperInvariant()} — {report.Summary()}";
            Status = string.Join("; ", report.Provenance.Select(p => p.ToString()));
        }
        catch (Exception ex) when (ex is ScriptParseException or FileNotFoundException
                                      or DirectoryNotFoundException)
        {
            RunSummary = null;
            Status = ex.Message;
        }
        finally
        {
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

    [RelayCommand]
    private void ClearLog()
    {
        Wire.Clear();
        Exceptions.Clear();
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (Steps.Count == 0)
        {
            Status = "run a script first";
            return;
        }

        Status = "reports are written by the command line: sasctl run <path> --report html";
        await Task.CompletedTask;
    }

    public void Dispose() => Disconnect();

    // ---- plumbing ------------------------------------------------------------------------

    private void OnWire(
        WireDirection direction,
        SasChannel channel,
        ReadOnlySpan<SasSymbol> symbols,
        TimeSpan elapsed,
        string? note)
    {
        // Rendered here, on the poll thread, because a span cannot be carried across a post.
        var line = new WireLine(
            $"{elapsed.TotalMilliseconds,9:F0}",
            direction == WireDirection.Transmit ? "TX" : "RX",
            (int)channel,
            string.Join(' ', symbols.ToArray().Select(s => s.ToString())),
            note ?? string.Empty);

        Dispatcher.UIThread.Post(() =>
        {
            Wire.Add(line);
            while (Wire.Count > WireLimit)
                Wire.RemoveAt(0);
        });
    }

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
        RefreshCounters();
    });

    private void RefreshCounters()
    {
        PollStatistics stats = _session?.Statistics ?? PollStatistics.Empty;
        Counters = $"{stats.SyncPolls} sync · {stats.GeneralPolls} general · {stats.LongPolls} long · "
                   + $"{stats.Exceptions} exceptions · {stats.Silences} silences";
    }
}

/// <summary>One message in the log.</summary>
public sealed record WireLine(string At, string Direction, int Channel, string Symbols, string Note);

/// <summary>One exception the machine reported.</summary>
public sealed record ExceptionLine(string Code, string Name, string Meaning, string At);

/// <summary>One meter, as read.</summary>
public sealed record MeterLine(string Name, string From, string Value, string Note);

/// <summary>One field of the last reply.</summary>
public sealed record FieldLine(string Name, string Value, string Note);

/// <summary>One line of a script run's result.</summary>
public sealed record StepLine(string Verdict, string What, string Where, string Detail);
