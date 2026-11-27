using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SasWorkbench.Qpm;

/// <summary>
/// The demo executable a run declared, found only by its full path.
/// </summary>
/// <remarks>
/// The platform is shared with other sessions' players, and a running demo is not necessarily the one under test, so
/// nothing here acts on a process found by name: only a process whose image is exactly the declared file is stopped or
/// sent a key, and anything else is refused with what was found.
/// </remarks>
public sealed class DemoProcess
{
    /// <summary>The window class every GLFW window registers, which is how the demo's own window is told from its console.</summary>
    private const string GlfwWindowClass = "GLFW30";

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;

    public DemoProcess(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ExecutablePath = Path.GetFullPath(executablePath);
    }

    /// <summary>The declared executable.</summary>
    public string ExecutablePath { get; }

    /// <summary>The demo's own log, which it writes beside the executable and flushes line by line.</summary>
    public string LogPath => Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "logs", "OpenGL_Game.log");

    /// <summary>Every running process whose image is the declared file; others of the same name are ignored.</summary>
    public IReadOnlyList<Process> Running()
    {
        var found = new List<Process>();
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutablePath)))
        {
            if (ImageOf(process) is { } image && string.Equals(image, ExecutablePath, StringComparison.OrdinalIgnoreCase))
                found.Add(process);
            else
                process.Dispose();
        }

        return found;
    }

    /// <summary>The one running demo at the declared path.</summary>
    /// <exception cref="QpmUnavailableException">None runs there, or more than one does.</exception>
    public Process RequireOne()
    {
        IReadOnlyList<Process> running = Running();
        if (running.Count == 1)
            return running[0];

        foreach (Process process in running)
            process.Dispose();

        throw new QpmUnavailableException(running.Count == 0
            ? $"no gaming machine is running from '{ExecutablePath}', so it cannot be driven from here"
            : $"{running.Count} gaming machines are running from '{ExecutablePath}', so which one to drive is unknown");
    }

    /// <summary>Stops the declared demo as a power cut would and starts it again from the same file.</summary>
    /// <returns>What was done, for the step's report.</returns>
    /// <exception cref="QpmUnavailableException">It was not running alone, would not stop, or would not stay up.</exception>
    public string Restart(TimeSpan stopWithin, TimeSpan stayUpFor)
    {
        if (!File.Exists(ExecutablePath))
            throw new QpmUnavailableException($"'{ExecutablePath}' does not exist, so the machine could not be started again");

        int stopped;
        using (Process running = RequireOne())
        {
            stopped = running.Id;
            running.Kill(entireProcessTree: false);
            if (!running.WaitForExit(stopWithin))
                throw new QpmUnavailableException($"the machine (process {stopped}) did not stop within {stopWithin.TotalSeconds:F0}s");
        }

        var start = new ProcessStartInfo(ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
            UseShellExecute = true,
        };

        using Process started = Process.Start(start)
            ?? throw new QpmUnavailableException($"'{ExecutablePath}' could not be started again");

        // A second platform refuses to start and exits at once, so a demo that is gone again is not one that booted.
        if (started.WaitForExit(stayUpFor))
        {
            throw new QpmUnavailableException(
                $"the machine started again from '{ExecutablePath}' but exited with code {started.ExitCode} within "
                + $"{stayUpFor.TotalSeconds:F0}s; its log says why");
        }

        return $"process {stopped} stopped and {started.Id} started from '{ExecutablePath}'";
    }

    /// <summary>How many lines the demo's log holds now, so a later read can look only at what came after.</summary>
    public int LogLineCount() => ReadLog().Length;

    /// <summary>Waits for a line after the first <paramref name="skip"/> that contains the text; null when none came.</summary>
    public string? WaitForLogLine(int skip, string contains, TimeSpan within)
    {
        long deadline = Environment.TickCount64 + (long)within.TotalMilliseconds;
        while (true)
        {
            string? line = ReadLog().Skip(skip).FirstOrDefault(l => l.Contains(contains, StringComparison.Ordinal));
            if (line is not null || Environment.TickCount64 >= deadline)
                return line;
            Thread.Sleep(100);
        }
    }

    /// <summary>Posts a key press and release to the demo's window, which acts on the release.</summary>
    /// <exception cref="QpmUnavailableException">There is no window to post to.</exception>
    public void PostKey(ushort virtualKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new QpmUnavailableException("keys are posted to the machine's window only on Windows");

        using Process demo = RequireOne();
        IntPtr window = WindowOf(demo.Id)
            ?? throw new QpmUnavailableException($"the machine running from '{ExecutablePath}' has no window to post a key to");

        // GLFW reads the key from the scan code in lParam, so it has to be the real one.
        uint scan = MapVirtualKey(virtualKey, 0);
        PostMessage(window, WmKeyDown, virtualKey, (IntPtr)(1 | (scan << 16)));
        Thread.Sleep(90);
        PostMessage(window, WmKeyUp, virtualKey, (IntPtr)(long)(0xC0000001u | (scan << 16)));
    }

    private string[] ReadLog()
    {
        try
        {
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Split('\n');
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ImageOf(Process process)
    {
        try
        {
            return process.MainModule?.FileName is { } file ? Path.GetFullPath(file) : null;
        }
        catch (Exception problem) when (problem is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static IntPtr? WindowOf(int processId)
    {
        IntPtr found = IntPtr.Zero;
        var name = new StringBuilder(64);
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId || !IsWindowVisible(window))
                return true;

            name.Clear();
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != GlfwWindowClass)
                return true;

            found = window;
            return false;
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : found;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, nuint wParam, IntPtr lParam);
}
