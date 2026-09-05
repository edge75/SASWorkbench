namespace SasWorkbench.Qpm;

/// <summary>How long the driver waits for things the machine has to do.</summary>
/// <param name="Liveness">
/// How long to watch a stopped heartbeat before deciding no machine is running.
/// </param>
/// <param name="Settle">How long to wait for a mask write to be noticed.</param>
public sealed record QpmTimings(TimeSpan Liveness, TimeSpan Settle)
{
    /// <summary>Timings that suit a machine polling its peripherals every 125ms.</summary>
    public static QpmTimings Default { get; } =
        new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
}

/// <summary>
/// Drives the gaming machine's peripherals through the shared region.
/// </summary>
/// <remarks>
/// <para>
/// Two rules make this safe to run beside a live machine. Every mask is composed locally and
/// written in one store, never cleared and rebuilt, because the machine reads all four masks on
/// its own clock and would see a mask being rebuilt as every door shutting and reopening. And
/// every mask this driver writes is one it alone owns: nothing here reads a field in order to
/// write it back.
/// </para>
/// <para>
/// Timing is taken from the machine, not from the wall clock. The machine samples its
/// peripherals once per heartbeat, so a button held for a fixed number of milliseconds is a
/// button that sometimes is not seen at all; a button held for two heartbeats always is.
/// </para>
/// </remarks>
public sealed class QpmMachine : IDisposable
{
    /// <summary>
    /// A bit of the printer mask the machine does not examine, used to find out whether anything
    /// else is writing the masks. The machine only ever looks at the four lowest printer bits, so
    /// setting this one cannot make it do anything.
    /// </summary>
    private const int InertProbeBit = 31;

    private readonly QpmSharedRegion _region;
    private readonly QpmTimings _timings;
    private readonly bool _ownsRegion;

    // What this driver has put in each mask. Deliberately not read back from the region: a
    // reading would pick up whatever another writer had done and quietly adopt it as our intent.
    private uint _intrusion;
    private uint _dio;
    private uint _bna;

    public QpmMachine(
        QpmSharedRegion region,
        EgmHardwareProfile profile,
        QpmTimings? timings = null,
        bool ownsRegion = true)
    {
        _region = region;
        _timings = timings ?? QpmTimings.Default;
        _ownsRegion = ownsRegion;
        Profile = profile;
    }

    /// <summary>What the machine says its own doors and buttons are.</summary>
    public EgmHardwareProfile Profile { get; }

    /// <summary>Where the shared region was opened from, for the run record.</summary>
    public string RegionPath => _region.Path;

    /// <summary>What the machine is trying to light.</summary>
    public IReadOnlyList<QpmLampState> Lamps => _region.Lamps();

    /// <summary>
    /// Opens the region, reads the machine's configuration and checks a machine is really there.
    /// </summary>
    /// <exception cref="QpmUnavailableException">Nothing usable is on the other end.</exception>
    public static QpmMachine Attach(
        string? regionPath = null,
        string? resourcesDirectory = null,
        QpmTimings? timings = null)
    {
        QpmSharedRegion region = QpmSharedRegion.Open(regionPath);
        try
        {
            var machine = new QpmMachine(
                region, EgmHardwareProfile.ReadFrom(resourcesDirectory), timings);

            machine.RequireRunningMachine();
            machine.RequireTheMasksAreOurs();
            return machine;
        }
        catch
        {
            region.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Refuses unless the machine's heartbeat is advancing.
    /// </summary>
    /// <remarks>
    /// Nothing about writing to the region fails when no machine is running: the write succeeds,
    /// the bytes sit there, and every assertion afterwards is made against a machine that never
    /// saw the stimulus. The heartbeat is the only evidence that anybody is reading.
    /// </remarks>
    /// <exception cref="QpmUnavailableException">Nothing is advancing the heartbeat.</exception>
    public void RequireRunningMachine()
    {
        if (WaitForHeartbeat(1, _timings.Liveness))
            return;

        throw new QpmUnavailableException(
            $"the peripheral heartbeat in '{_region.Path}' has not advanced in "
            + $"{_timings.Liveness.TotalSeconds:F0}s, so no gaming machine is reading it: "
            + "stimulus written now would be silently lost");
    }

    /// <summary>
    /// Refuses when something else is writing the stimulus masks.
    /// </summary>
    /// <remarks>
    /// The peripheral simulator's own interface rewrites all four masks from its checkboxes on
    /// every frame it draws, so a third party's writes survive about sixteen milliseconds. Racing
    /// it produces a run that fails at random, which is far worse than one that refuses to start.
    /// </remarks>
    /// <exception cref="QpmUnavailableException">Somebody else owns the masks.</exception>
    public void RequireTheMasksAreOurs()
    {
        // The printer mask is the one nothing else in this driver touches, so probing it cannot
        // disturb a door or a note that a test has already set up.
        const uint probe = 1u << InertProbeBit;
        _region.Write(QpmLayout.Printer, probe);

        // Long enough for a sixty-hertz redraw to have happened several times over.
        Thread.Sleep(100);
        uint after = _region.Read(QpmLayout.Printer);
        _region.Write(QpmLayout.Printer, 0);

        if (after == probe)
            return;

        throw new QpmUnavailableException(
            $"something else is writing the peripheral masks in '{_region.Path}': a value written "
            + "here was gone within 100ms. The peripheral simulator's window rewrites all four "
            + "masks on every frame, so close it before an unattended run");
    }

    /// <summary>
    /// Brings the machine to a known state: every door shut, the note acceptor connected.
    /// </summary>
    /// <remarks>
    /// This is deliberately something a test asks for rather than something that happens to it.
    /// The machine detects a door by the change in the mask, so a door it believes is open at
    /// power-up — and this machine believes several are — can only be shut by driving it open and
    /// then shut again, which puts a real pair of door exceptions on the wire. A test that did
    /// not ask for those would be right to be surprised by them.
    /// </remarks>
    /// <returns>What was done, in the order it was done, for the report.</returns>
    public IReadOnlyList<string> Settle()
    {
        var done = new List<string>();

        foreach (EgmDoor door in Profile.Doors)
        {
            SetDoor(door, open: true);
            SetDoor(door, open: false);
            done.Add($"door {door.Type} (intrusion {door.Intrusion}) opened, then closed");
        }

        SetBnaBit(QpmLayout.BnaDisconnectedBit, on: true);
        SetBnaBit(QpmLayout.BnaDisconnectedBit, on: false);
        done.Add("note acceptor disconnected, then reconnected");

        if (done.Count == 1)
        {
            done.Add("no doors were touched: "
                + (Profile.UnavailableBecause ?? "the machine declares none"));
        }

        return done;
    }

    /// <summary>Opens or closes a door and waits for the machine to have sampled it.</summary>
    public void SetDoor(EgmDoor door, bool open)
    {
        _intrusion = Apply(_intrusion, door.Intrusion, open);
        _region.Write(QpmLayout.Intrusion, _intrusion);
        WaitTwoHeartbeats();
    }

    /// <summary>
    /// Presses a button and lets go of it.
    /// </summary>
    /// <remarks>
    /// Held for two heartbeats so the machine sees a rising edge and a falling edge as two
    /// separate samples. A press and release inside one heartbeat is a press the machine never
    /// saw, and a test built on it would fail perhaps one time in two.
    /// </remarks>
    public void PressButton(EgmButton button)
    {
        SetButton(button, pressed: true);
        SetButton(button, pressed: false);
    }

    /// <summary>Holds a button down, or lets it go, without pulsing it.</summary>
    /// <remarks>A keyswitch is turned and left turned, so it is not a press.</remarks>
    public void SetButton(EgmButton button, bool pressed)
    {
        _dio = Apply(_dio, button.DioLine, pressed);
        _region.Write(QpmLayout.Dio, _dio);
        WaitTwoHeartbeats();
    }

    /// <summary>
    /// Offers a note of this value to the acceptor.
    /// </summary>
    /// <exception cref="QpmUnavailableException">The machine has no bit for that value.</exception>
    public void InsertNote(int cents)
    {
        int bit = QpmLayout.NoteBit(cents)
            ?? throw new QpmUnavailableException(
                $"this machine's note acceptor has no {cents}-cent note: it reads only "
                + $"{QpmLayout.NoteValuesAsText()} (in cents)");

        SetBnaBit(bit, on: true);
        SetBnaBit(bit, on: false);
    }

    /// <summary>Reports the note acceptor as jammed, or clears the jam.</summary>
    public void SetNoteAcceptorJammed(bool jammed) =>
        SetBnaBit(QpmLayout.BnaJamBit, jammed);

    /// <summary>Waits until the heartbeat has advanced this many times.</summary>
    /// <returns>False when it stopped advancing before then.</returns>
    public bool WaitForHeartbeat(int ticks, TimeSpan timeout)
    {
        uint start = _region.Heartbeat;
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (unchecked(_region.Heartbeat - start) < (uint)ticks)
        {
            if (Environment.TickCount64 > deadline)
                return false;

            Thread.Sleep(5);
        }

        return true;
    }

    public void Dispose()
    {
        if (_ownsRegion)
            _region.Dispose();
    }

    private void SetBnaBit(int bit, bool on)
    {
        _bna = Apply(_bna, bit, on);
        _region.Write(QpmLayout.Bna, _bna);
        WaitTwoHeartbeats();
    }

    private static uint Apply(uint mask, int bit, bool on) =>
        on ? mask | (1u << bit) : mask & ~(1u << bit);

    private void WaitTwoHeartbeats()
    {
        if (WaitForHeartbeat(2, _timings.Settle))
            return;

        throw new QpmUnavailableException(
            "the gaming machine stopped reading its peripherals part-way through this step, so "
            + "what it did with it is unknown");
    }
}
