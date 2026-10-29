using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Scripting;
using SasWorkbench.Core.Session;
using SasWorkbench.Core.Wire;
using SasWorkbench.Egm;
using SasWorkbench.Gui;
using SasWorkbench.Gui.Controls;
using SasWorkbench.Gui.ViewModels;
using SasWorkbench.Gui.Views;

namespace SasWorkbench.UiSpecimen;

/// <summary>
/// Renders the window off-screen so a design change can be looked at rather than argued about.
/// </summary>
/// <remarks>
/// Both theme variants and both window sizes, filled with the captured frames from the first
/// milestone rather than with invented content, so what the picture shows is what the tool shows.
/// Development only: nothing here ships in a release.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        string directory = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "specimens");
        Directory.CreateDirectory(directory);

        // Binding failures are silent at runtime, so the specimen run is also the place they get
        // caught: a mistyped binding renders an empty cell and nothing else says so.
        AppBuilder.Configure<App>()
            .LogToTrace(LogEventLevel.Warning)
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        //  The instruments the matrix below does not reach, once each. Rendered first because
        //  the matrix runs the committed suite for every one of its verdict pictures, and a
        //  binding error is worth hearing about before that rather than after it. A tab control realises the
        //  selected tab's content and nothing else, so an instrument no specimen renders is one
        //  whose bindings nothing checks - which is the whole reason this run turns binding
        //  diagnostics on. One size and one lane setting each: what is being checked here is that
        //  they bind and lay out, not how they compare.
        foreach ((string themeName, ThemeVariant variant) in
                 new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            foreach ((string tabName, int tab) in new[]
                     {
                         ("stimulus", 4), ("record", 5), ("meters", 1), ("exceptions", 2),
                     })
            {
                string only = Path.Combine(directory, $"instrument-{themeName}-{tabName}.png");
                Capture(variant, 1180, 760, tab, allLanes: false, only);
                Console.WriteLine(only);
            }
        }

        foreach ((string themeName, ThemeVariant variant) in
                 new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            foreach ((string sizeName, int width, int height) in
                     new[] { ("1180x760", 1180, 760), ("900x600", 900, 600) })
            {
                foreach ((string tabName, int tab) in new[] { ("longpoll", 0), ("run", 3) })
                {
                    foreach ((string lanes, bool all) in new[] { ("wire", false), ("lanes", true) })
                    {
                        string path = Path.Combine(
                            directory, $"window-{themeName}-{sizeName}-{tabName}-{lanes}.png");
                        Capture(variant, width, height, tab, all, path);
                        Console.WriteLine(path);
                    }
                }
            }
        }

        // A search, highlighted and then filtered, so the two behaviours can be compared rather
        // than described.
        foreach ((string themeName, ThemeVariant variant) in
                 new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            foreach ((string what, string query, bool only) in new[]
                     {
                         ("highlight", "lp 1F", false),
                         ("filter", "lp 1F", true),
                     })
            {
                string found = Path.Combine(directory, $"find-{themeName}-{what}.png");
                Capture(variant, 1180, 760, 0, allLanes: true, found, query, only);
                Console.WriteLine(found);
            }
        }

        foreach ((string themeName, ThemeVariant variant) in
                 new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            string path = Path.Combine(directory, $"chips-{themeName}.png");
            CaptureChips(variant, path);
            Console.WriteLine(path);
        }

        return 0;
    }

    /// <summary>
    /// Every chip state on one sheet, so the three redundant cues can be checked rather than
    /// assumed: the word, the failure-only solid fill, and the glyph.
    /// </summary>
    private static void CaptureChips(ThemeVariant variant, string path)
    {
        var chips = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        foreach (ChipStatus status in Enum.GetValues<ChipStatus>())
            chips.Children.Add(new StatusChip { Status = status, Reason = "a reason" });

        chips.Children.Add(new ProvenanceSeal { IsProven = true });
        chips.Children.Add(new ProvenanceSeal
        {
            IsProven = false,
            Caveat = "no adapter has been measured",
        });

        var window = new Window
        {
            Content = new Border { Classes = { "panel" }, Child = chips },
            RequestedThemeVariant = variant,
            Background = null,
            Width = 260,
            Height = 260,
            SystemDecorations = SystemDecorations.None,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the sheet rendered no frame");
        using FileStream file = File.Create(path);
        frame.Save(file);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(
        ThemeVariant variant,
        int width,
        int height,
        int instrument,
        bool allLanes,
        string path,
        string? find = null,
        bool onlyMatches = false)
    {
        WorkbenchViewModel model = Specimen();
        model.SelectedInstrument = model.Instruments[instrument];

        if (allLanes)
        {
            foreach (LaneFilter lane in model.Stream.Lanes)
                lane.IsShown = true;
        }

        if (find is not null)
        {
            model.Stream.Find = find;
            model.Stream.HideOthers = onlyMatches;
        }

        var window = new MainWindow
        {
            DataContext = model,
            RequestedThemeVariant = variant,
            Width = width,
            Height = height,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered no frame");
        using FileStream file = File.Create(path);
        frame.Save(file);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The captured content from `docs/M1-thin-vertical-slice.md`. Nothing is invented.</summary>
    private static WorkbenchViewModel Specimen()
    {
        var model = new WorkbenchViewModel
        {
            SelectedTransport = "tcp",
            Target = "127.0.0.1:9760",
            LinkState = "up",
            IsLinkUp = true,
            IsConnected = true,
            IsLinkProven = true,
            LinkClaim = "tcp 127.0.0.1:9760 (listen) · stimulus none",
            Counters = "34 sync · 34 general · 2 long · 1 exceptions · 0 silences",
            LogBound = "log 9/20000",
            Status = "connected to tcp 127.0.0.1:9760 (listen)",
            LastReply = "LP $1F ch1 Answered 011F515830303102FF00000030303100000039323130863F (30.7ms)",
            RunSummary = "PASS — 1 passed, 1 skipped in 7.0s",
        };

        model.Stream.Watch(Captured());
        Ran(model);

        model.Fields.Add(new FieldLine("game_id", "QX", string.Empty));
        model.Fields.Add(new FieldLine("additional_id", "001", string.Empty));
        model.Fields.Add(new FieldLine("denomination", "2", "Code"));
        model.Fields.Add(new FieldLine("max_bet", "255", "Credits"));
        model.Fields.Add(new FieldLine("progressive_group", "0", "Index"));
        model.Fields.Add(new FieldLine("game_options", "0", string.Empty));
        model.Fields.Add(new FieldLine("paytable_id", "001", string.Empty));
        model.Fields.Add(new FieldLine("base_percentage", "9210", "HundredthsOfPercent"));

        model.Meters.Add(new MeterLine("total_bill_value", "$20", "2000", "credits"));
        model.Meters.Add(new MeterLine("games_played", "$15", "14", "count"));
        model.Meters.Add(new MeterLine("current_credits", "$1A", "340", "credits"));
        model.Meters.Add(new MeterLine("serial_number", "$54", "—",
            "the reply held no 'serial_number'"));

        model.Exceptions.Add(new ExceptionLine("$4A", "bill_accepted",
            "a bill was accepted by the machine", "6200 ms"));

        return model;
    }

    /// <summary>
    /// The suite's verdicts, run once for the whole specimen rather than once per image.
    /// </summary>
    /// <remarks>
    /// Every capture builds a fresh view model, because a capture mutates the one it is given. The
    /// <em>run</em> behind it does not have to be fresh: the verdicts are the same picture every
    /// time, so running the suite again for each image was two dozen identical runs to draw two
    /// dozen pictures of the same run.
    /// </remarks>
    private static readonly Lazy<RunReport?> Suite_ = new(RunTheSuite);

    /// <summary>
    /// Runs the committed regression suite against the software machine, so the picture shows real
    /// verdicts rather than invented ones.
    /// </summary>
    private static void Ran(WorkbenchViewModel model)
    {
        if (Suite_.Value is not { } report)
        {
            model.RunSummary = Refusal ?? "the suite could not be run for this specimen";
            return;
        }

        model.Suite.Apply(report);
        model.RunSummary = $"{report.Verdict.ToString().ToUpperInvariant()} — {report.Summary()}";
        model.Suite.Selected = model.Suite.Nodes
            .SelectMany(n => n.Children)
            .SelectMany(n => n.Children)
            .FirstOrDefault(n => n.Outcome is not null);
    }

    /// <summary>Why the suite could not be run, when it could not.</summary>
    private static string? Refusal;

    private static RunReport? RunTheSuite()
    {
        try
        {
            //  The floor cases are left out. A round of a hundred machines is a minute of work to
            //  draw a window that shows verdicts, and the picture is no better for it — the
            //  instrument renders the same either way. They are run by the suites, which is where
            //  they belong.
            IReadOnlyList<ScriptDocument> documents =
            [
                .. ScriptLoader.Load(Suite())
                    .Select(document => document with
                    {
                        Tests = [.. document.Tests.Where(test => !test.Tags.Contains("casino", StringComparer.OrdinalIgnoreCase))],
                    })
                    .Where(document => document.Tests.Count > 0),
            ];

            var runner = new ScriptRunner(new ScriptRunnerOptions
            {
                Stimulus = StimulusBackend.Software,
                Environment = SoftwareMachineEnvironment.For(),
                // The window's own default, so the verdicts are the ones a person running the suite sees.
                PollInterval = TimeSpan.FromMilliseconds(200),
            });

            return runner.RunAll(documents);
        }
        catch (Exception ex) when (ex is ScriptParseException or IOException)
        {
            Refusal = $"the suite could not be run for this specimen: {ex.Message}";
            return null;
        }
    }

    private static string Suite()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "regression");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return "tests";
    }

    /// <summary>The frames the first milestone actually captured, replayed into a session log.</summary>
    private static SessionLog Captured()
    {
        var log = new SessionLog();
        log.TestStarted("link-up", SourceLocation.None, SasChannel.One, TimeSpan.Zero);

        foreach ((double at, WireDirection direction, string hex, string note) in Frames)
        {
            SasSymbol[] symbols = [.. hex.Split(' ').Select(Parse)];
            log.Wire(direction, SasChannel.One, null, symbols, TimeSpan.FromMilliseconds(at),
                ClockBase.Session, note.Length == 0 ? null : note);

            if (at == 5985)
            {
                log.Exception(
                    new ExceptionReport(0x18, SasChannel.One, TimeSpan.FromMilliseconds(5986), 1),
                    ClockBase.Session, "gaming machine out of service");
            }
        }

        log.Step(
            new StepResult(new SourceLocation("link-up.sas", 5), "expect field game_id == QX",
                Verdict.Pass, "QX", "QX", At: TimeSpan.FromMilliseconds(6200))
            {
                IsAssertion = true,
            },
            SasChannel.One,
            ClockBase.Session,
            TimeSpan.FromMilliseconds(6230));

        return log;
    }

    private static SasSymbol Parse(string symbol)
    {
        byte data = Convert.ToByte(symbol[2..], 16);
        return symbol[0] == '1' ? SasSymbol.Address(data) : SasSymbol.Data8(data);
    }

    private static readonly (double At, WireDirection Direction, string Hex, string Note)[] Frames =
    [
        (5927, WireDirection.Transmit, "1.80", "sync poll - address zero"),
        (5954, WireDirection.Transmit, "1.81", "general poll"),
        (5985, WireDirection.Receive, "0.18", "exception $18"),
        (6126, WireDirection.Transmit, "1.80", "sync poll - address zero"),
        (6157, WireDirection.Transmit, "1.01 0.1F", "long poll $1F"),
        (6189, WireDirection.Receive,
            "0.01 0.1F 0.51 0.58 0.30 0.30 0.31 0.02 0.FF 0.00 0.00 0.00 0.30 0.30 0.31 0.00 "
            + "0.00 0.00 0.39 0.32 0.31 0.30 0.86 0.3F", ""),
        (6730, WireDirection.Transmit, "1.80", "sync poll - address zero"),
        (6761, WireDirection.Transmit, "1.01 0.54", "long poll $54"),
        (6791, WireDirection.Receive, "0.01 0.54 0.03 0.36 0.30 0.32 0.D0 0.8D", ""),
    ];
}
