using SasWorkbench.Core.Diagnostics;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Captures;

/// <summary>Which tool wrote a capture, and therefore how it is read.</summary>
public enum CaptureFormat
{
    /// <summary>The conformance toolkit's textual trace, one message per line.</summary>
    GsaTrace,

    /// <summary>The conformance toolkit's binary session log.</summary>
    GsaHostLog,

    /// <summary>The legacy tester's textual serial log.</summary>
    IgtSerialLog,
}

/// <summary>One message as some other tool recorded it.</summary>
/// <param name="Direction">Which end sent it.</param>
/// <param name="Symbols">Its symbols, wakeup bits exactly as the capture recorded them.</param>
/// <param name="At">When it happened, measured from the start of the capture.</param>
public sealed record CapturedMessage(WireDirection Direction, SasSymbol[] Symbols, TimeSpan At);

/// <summary>
/// Traffic nobody here authored, read back off another tool's recording.
/// </summary>
/// <remarks>
/// <para>
/// The point of reading these is that our own suite can only exchange what it was taught to
/// exchange, so a coverage figure taken over it measures the script rather than the protocol. A
/// capture from another tool driving a real machine does not flatter us.
/// </para>
/// <para>
/// Only the bytes, the direction and the timing are taken. Every capture format also carries the
/// other tool's own prose describing each message, and none of it is ingested: the words in this
/// product are ours, and a description written by somebody else would be indistinguishable from
/// one of ours once it was in the record.
/// </para>
/// </remarks>
public sealed record Capture
{
    /// <summary>Where it came from, as a reader can check.</summary>
    public required string Source { get; init; }

    /// <summary>Which tool wrote it.</summary>
    public required CaptureFormat Format { get; init; }

    /// <summary>
    /// Whether the recording kept the ninth bit. Two of the three formats do not, and a reader
    /// who is not told that would read every message in them as one no machine would answer.
    /// </summary>
    public required bool WakeupBitCaptured { get; init; }

    /// <summary>When the capture began, when it says. Null when it does not.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>What the capture says about its own timing, when it needs a caveat.</summary>
    public string? TimingCaveat { get; init; }

    /// <summary>The messages, in the order they were recorded.</summary>
    public required IReadOnlyList<CapturedMessage> Messages { get; init; }

    /// <summary>
    /// Everything in the file this build could not read, named rather than skipped. A reader that
    /// dropped what it did not understand would make a partial import look like a complete one.
    /// </summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];

    /// <summary>How long the capture spans, by its own timestamps.</summary>
    public TimeSpan Duration => Messages.Count == 0 ? TimeSpan.Zero : Messages[^1].At;

    /// <summary>The messages as wire records, which is the shape everything downstream reads.</summary>
    public IReadOnlyList<WireRecord> ToWire(SasChannel channel = SasChannel.One)
    {
        var records = new List<WireRecord>(Messages.Count);
        for (int i = 0; i < Messages.Count; i++)
        {
            CapturedMessage message = Messages[i];
            records.Add(new WireRecord(
                i + 1, message.Direction, channel, message.At, message.Symbols, null));
        }

        return records;
    }
}
