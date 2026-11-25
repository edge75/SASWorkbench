using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>One ROM signature a machine sent.</summary>
/// <param name="Signature">The signature, as the machine computed it.</param>
/// <param name="Channel">Which channel it arrived on.</param>
/// <param name="Elapsed">Time since the engine started.</param>
/// <param name="OnLongPoll">True when it took a long poll's window rather than answering a general poll.</param>
public sealed record RomSignatureReport(ushort Signature, SasChannel Channel, TimeSpan Elapsed, bool OnLongPoll)
{
    /// <summary>The machine that sent it.</summary>
    public SasAddress? Address { get; init; }

    public override string ToString() =>
        $"ROM signature ${Signature:X4} ch{(int)Channel} at {Elapsed.TotalMilliseconds:F0}ms"
        + (OnLongPoll ? " in a long poll's window" : " on a general poll");
}

/// <summary>Hearing the ROM signature a long poll $21 asked for, which the machine sends later and unasked.</summary>
public sealed partial class PollEngine
{
    // Address, $21, two bytes of signature and the check sequence (sasThread's pending ROM signature branch).
    private const int RomSignatureLength = 6;

    /// <summary>Whether a frame is the ROM signature reply addressed to the machine being polled.</summary>
    private bool IsRomSignature(ReadOnlySpan<byte> frame) =>
        frame.Length == RomSignatureLength
        && frame[0] == _polling.Value
        && frame[1] == (byte)SasLongPollCode.LP_ROMVERIFICATION;

    /// <summary>Reads a general poll answered with the ROM signature, and reports it when its check sequence holds.</summary>
    private GeneralPollResult ReadRomSignature(byte[] frame, bool wakeup, TimeSpan latency)
    {
        if (wakeup || !SasCrc.Verify(frame))
        {
            RecordReceive(frame.Length, wakeup,
                "a ROM signature whose check sequence does not check out, so no byte of it is established");
            return new GeneralPollResult(_polling, _options.Channel, GeneralPollOutcome.Unreadable, 0x00, frame,
                latency, "The message looked like a ROM signature and failed its check sequence.");
        }

        RecordReceive(frame.Length, wakeup, $"ROM signature ${frame[2]:X2}{frame[3]:X2}");
        MarkHeard();
        ReportRomSignature(frame, onLongPoll: false);
        return new GeneralPollResult(_polling, _options.Channel, GeneralPollOutcome.RomSignature,
            (byte)SasLongPollCode.LP_ROMVERIFICATION, frame, latency)
        {
            Data = frame[2..4],
        };
    }

    /// <summary>Tells whoever listens that a signature arrived; the frame has already been checked.</summary>
    private void ReportRomSignature(byte[] frame, bool onLongPoll)
    {
        var report = new RomSignatureReport((ushort)((frame[2] << 8) | frame[3]), _options.Channel,
            _clock.Elapsed, onLongPoll)
        {
            Address = _polling,
        };
        RomSignatureReported?.Invoke(report);
    }
}
