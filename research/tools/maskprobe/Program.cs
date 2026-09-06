// Samples one 32-bit field of the peripheral shared region as fast as it can, and reports every
// distinct value it observed together with how long each one was actually held. A mask that is
// cleared and rebuilt in place shows up as extra values with a tiny residency; a mask published
// in one store shows exactly one.
//
// The region persists after its writer exits, so a dead simulator looks exactly like a
// well-behaved one: both give a constant value. Liveness is therefore proved rather than assumed,
// by writing a sentinel and waiting for the simulator's own 60 Hz rewrite to erase it — the
// clobber of Trap A, used here as the signal that a writer is present. It is checked before and
// after sampling, so a writer that dies midway cannot be reported as a clean run.
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

const string RegionPath = @"C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm";
const int IntrusionOffset = 0x004;
const uint Sentinel = 0xA5A5A5A5;

int seconds = args.Length > 0 ? int.Parse(args[0]) : 10;
string label = args.Length > 1 ? args[1] : "run";

using var stream = new FileStream(RegionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
using var mapped = MemoryMappedFile.CreateFromFile(
    stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
using var view = mapped.CreateViewAccessor(0, 1024, MemoryMappedFileAccess.ReadWrite);

unsafe
{
    byte* basePtr = null;
    view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
    uint* slot = (uint*)(basePtr + IntrusionOffset);

    bool WriterIsAlive(out TimeSpan took)
    {
        uint held = Volatile.Read(ref *slot);
        Volatile.Write(ref *slot, Sentinel);
        var w = Stopwatch.StartNew();
        while (w.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (Volatile.Read(ref *slot) != Sentinel) { took = w.Elapsed; return true; }
            Thread.Sleep(1);
        }
        Volatile.Write(ref *slot, held);   // put it back; nobody else will
        took = w.Elapsed;
        return false;
    }

    // Nothing to measure while every door is unticked: the mask is a constant zero either way.
    var waited = Stopwatch.StartNew();
    while (Volatile.Read(ref *slot) == 0)
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(180))
        {
            Console.WriteLine($"[{label}] NO SIGNAL: the intrusion mask stayed zero for 180 s, so nothing was ticked.");
            return 2;
        }
        Thread.Sleep(20);
    }

    if (!WriterIsAlive(out var before))
    {
        Console.WriteLine($"[{label}] NO WRITER: a sentinel written into the mask survived 2 s untouched, "
                        + "so the simulator is not running and this region is only a stale file. Nothing measured.");
        return 3;
    }
    Console.WriteLine($"[{label}] writer alive (sentinel erased in {before.TotalMilliseconds:F0} ms); sampling for {seconds} s.");

    var changes = new List<(uint Value, long At)>(1 << 22);
    long samples = 0;
    uint last = Volatile.Read(ref *slot);
    changes.Add((last, 0));

    var clock = Stopwatch.StartNew();
    var limit = TimeSpan.FromSeconds(seconds);
    while (clock.Elapsed < limit)
    {
        for (int i = 0; i < 2000; i++)
        {
            uint v = Volatile.Read(ref *slot);
            samples++;
            if (v != last)
            {
                last = v;
                if (changes.Count < changes.Capacity)
                    changes.Add((v, samples));
            }
        }
    }
    clock.Stop();

    if (!WriterIsAlive(out _))
    {
        Console.WriteLine($"[{label}] WRITER DIED DURING THE RUN: the simulator stopped writing before the sample ended. "
                        + "Nothing measured.");
        return 3;
    }

    // Residency: how many samples each value was actually held for, across all its intervals.
    var residency = new Dictionary<uint, long>();
    var entries = new Dictionary<uint, long>();
    for (int i = 0; i < changes.Count; i++)
    {
        long end = i + 1 < changes.Count ? changes[i + 1].At : samples;
        uint v = changes[i].Value;
        residency[v] = residency.GetValueOrDefault(v) + (end - changes[i].At);
        entries[v] = entries.GetValueOrDefault(v) + 1;
    }

    double rate = samples / clock.Elapsed.TotalSeconds;
    Console.WriteLine($"[{label}] {samples:N0} samples in {clock.Elapsed.TotalSeconds:F2} s = {rate / 1e6:F1} M/s");
    Console.WriteLine($"[{label}] transitions observed: {changes.Count - 1:N0}");
    Console.WriteLine($"[{label}] distinct values: {residency.Count}");
    Console.WriteLine();
    // A person ticking a checkbox holds the new value for hundreds of milliseconds; a mask being
    // rebuilt in place holds an intermediate value for well under a microsecond. Six orders of
    // magnitude apart, so mean hold per entry separates them without any judgement call.
    double secondsPerSample = clock.Elapsed.TotalSeconds / samples;
    var glitches = new List<uint>();

    Console.WriteLine($"  {"value",10}  {"binary",-18} {"entered",8}  {"residency (ppm)",16}  {"mean hold",14}");
    foreach (var kv in residency.OrderByDescending(k => k.Value))
    {
        double ppm = 1e6 * kv.Value / samples;
        double meanHoldUs = 1e6 * secondsPerSample * kv.Value / entries[kv.Key];
        bool glitch = meanHoldUs < 1000.0;
        if (glitch) glitches.Add(kv.Key);
        Console.WriteLine($"  {"0x" + kv.Key.ToString("X4"),10}  {Convert.ToString(kv.Key, 2).PadLeft(16, '0'),-18} " +
                          $"{entries[kv.Key],8:N0}  {ppm,16:F3}  {meanHoldUs,11:F2} us{(glitch ? "   <-- GLITCH" : "")}");
    }

    Console.WriteLine();
    if (glitches.Count == 0)
    {
        Console.WriteLine($"[{label}] CLEAN: every value observed was held long enough to be a deliberate change.");
        return 0;
    }

    Console.WriteLine($"[{label}] RACE: {glitches.Count} value(s) held for under a millisecond — "
                    + string.Join(", ", glitches.Select(g => "0x" + g.ToString("X4")))
                    + " — which no hand can produce.");
    return 1;
}
