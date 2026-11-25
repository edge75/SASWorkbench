using System.Diagnostics;
using SasWorkbench.Core.Protocol;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// The line itself: the loop-break chirp, the poll address the operator sets, and the clock the host sets.
/// </summary>
/// <remarks>
/// <para>
/// The chirp is the library's: a channel that has heard no message for five seconds sends its own
/// address with the wakeup bit every 200 ms until a message arrives, whoever it is addressed to, and a
/// channel at address 0 never chirps (libsas_threads.cpp:2410, :2548, libsas_uart.cpp:669, :1614).
/// </para>
/// <para>
/// The address is the platform's: saving a changed address restarts the library at it and reports $3C,
/// and an address of 0 starts no library at all (QSP SasNetwork.cpp:331, :753, :855).
/// </para>
/// </remarks>
public sealed partial class FakeEgm
{
    /// <summary>How often a broken loop chirps, the library's timer tick (libsas_threads.cpp:2409).</summary>
    private static readonly TimeSpan ChirpInterval = TimeSpan.FromMilliseconds(200);

    // How far the host has moved the machine's clock from the computer's own.
    private TimeSpan _clockOffset;

    /// <summary>How many chirps this machine has sent, on every channel.</summary>
    public long Chirps => Interlocked.Read(ref _chirps);

    private long _chirps;

    /// <summary>The machine's own clock, as the host last set it.</summary>
    public DateTime Now
    {
        get
        {
            lock (_gate)
                return DateTime.Now + _clockOffset;
        }
    }

    /// <summary>
    /// The operator sets the first channel's poll address; null when done, otherwise why not.
    /// </summary>
    /// <remarks>
    /// Saving the same address restarts nothing (QSP SasRules.h:412). A changed one restarts the library:
    /// the queue is kept with nothing in it sent, and the link waits for a synchronisation poll, as at a
    /// power-up (libsas_threads.cpp:1518). At address 0 the library is not started, so nothing is heard
    /// and no $3C can be written.
    /// </remarks>
    public string? SetPollAddress(SasAddress address)
    {
        bool restarted;
        lock (_gate)
        {
            MachineLink link = _links[0];
            if (link.Address == address)
                return null;

            link.Address = address;
            link.Exceptions.Refuse();
            RestartLink(link);
            restarted = !address.IsSync;
        }

        // QSP SasNetwork.cpp:753 raises $3C once the restarted library is up.
        if (restarted)
            QueueOrdinary((byte)SasExceptionCode.OperatorChangedOptions);
        return null;
    }

    /// <summary>
    /// Sends the loop-break chirp when it is due: five seconds of nothing heard, then every 200 ms.
    /// </summary>
    private void TickChirp(MachineLink link)
    {
        if (_options.LinkLossAfter <= TimeSpan.Zero || link.Address.IsSync)
            return;

        long now = Stopwatch.GetTimestamp();
        long since = link.LastHeard != 0 ? link.LastHeard : link.ListeningSince;
        if (Stopwatch.GetElapsedTime(since, now) <= _options.LinkLossAfter)
            return;

        if (link.NextChirp != 0 && now < link.NextChirp)
            return;

        link.NextChirp = now + (long)(ChirpInterval.TotalSeconds * Stopwatch.Frequency);
        Interlocked.Increment(ref _chirps);
        Send(link, [SasSymbol.Address(link.Address.Value)]);
    }

    /// <summary>
    /// $7F, as lp7F_ReceiveDateAndTime takes it: the fields checked, refused when out of range,
    /// acknowledged only when addressed, then handed to the platform.
    /// </summary>
    /// <remarks>
    /// The address and the check sequence were tested before this is reached (lp.cpp:7752, :7756).
    /// </remarks>
    private void AnswerReceiveDateAndTime(MachineLink link, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 11)
        {
            Reject(link);
            return;
        }

        int month = FromBcd(frame[2]);
        int day = FromBcd(frame[3]);
        int year = (FromBcd(frame[4]) * 100) + FromBcd(frame[5]);
        int hour = FromBcd(frame[6]);
        int minute = FromBcd(frame[7]);
        int second = FromBcd(frame[8]);

        // lp.cpp:7772 refuses a field out of range.
        if (month is < 1 or > 12 || day is < 1 or > 31 || year < 1900
            || hour > 23 || minute > 59 || second > 59)
        {
            Reject(link);
            return;
        }

        // lp.cpp:7790 acknowledges only a poll addressed to the machine; a broadcast is taken silently.
        if (frame[0] == link.Address.Value)
            Send(link, [SasSymbol.Data8(link.Address.Value)]);

        // QSP SasNetwork_Callbacks.hpp:799 leaves the clock alone for a date its calendar cannot place.
        if (year > 9999 || day > DateTime.DaysInMonth(year, month))
            return;

        var sent = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        lock (_gate)
            _clockOffset = sent - DateTime.Now;
    }

    /// <summary>One binary coded decimal byte as a number, each nibble read as a digit.</summary>
    private static int FromBcd(byte value) => ((value >> 4) * 10) + (value & 0x0F);
}
