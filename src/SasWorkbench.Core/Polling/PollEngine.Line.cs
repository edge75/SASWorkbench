using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Polling;

/// <summary>One message heard on a line nobody had polled, and when.</summary>
/// <param name="At">When it arrived, on the engine's clock.</param>
/// <param name="Frame">Its bytes.</param>
/// <param name="Wakeup">True when a symbol of it carried the wakeup bit.</param>
public sealed record HeardMessage(TimeSpan At, byte[] Frame, bool Wakeup);

/// <summary>Listening to a quiet line, and following the machine to a new address.</summary>
public sealed partial class PollEngine
{
    /// <summary>
    /// Sends nothing and records whatever the machine sends, for a while.
    /// </summary>
    /// <remarks>
    /// Only in the sequenced mode, where the engine's thread is idle between the messages a caller
    /// names: outside it the round is on the line and a quiet line cannot be had. Nothing heard here
    /// marks the link up, because a chirp is a machine saying it has lost the host.
    /// </remarks>
    /// <param name="duration">How long to listen.</param>
    /// <param name="enough">Stops early once what has been heard satisfies it.</param>
    /// <returns>What was heard, or null when the engine is not in a sequence.</returns>
    public IReadOnlyList<HeardMessage>? Listen(TimeSpan duration, Func<IReadOnlyList<HeardMessage>, bool>? enough = null)
    {
        if (!_sequenced || !_running)
            return null;

        var heard = new List<HeardMessage>();
        _executeGate.Wait();
        try
        {
            _attributed = null;
            TimeSpan until = _clock.Elapsed + duration;
            while (true)
            {
                TimeSpan remaining = until - _clock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    return heard;

                int received = _link.Receive(_inbound, remaining, _options.QuietGap, out bool wakeup);
                if (received == 0)
                    return heard;

                heard.Add(new HeardMessage(_clock.Elapsed, _inbound.AsSpan(0, received).ToArray(), wakeup));
                RecordReceive(received, wakeup, IsChirp(_inbound.AsSpan(0, received)) && wakeup
                    ? "loop-break chirp - the machine is sending its own address"
                    : "heard on a line nobody polled");

                if (enough?.Invoke(heard) == true)
                    return heard;
            }
        }
        finally
        {
            _executeGate.Release();
        }
    }

    /// <summary>
    /// Follows the one machine this host polls to the address it has been given at the machine.
    /// </summary>
    /// <returns>Null when done, otherwise why not.</returns>
    public string? Readdress(SasAddress address)
    {
        if (address.IsSync)
            return "address 00 is the synchronisation address, which no machine answers at";

        if (_options.Round.Count != 1)
            return $"this host polls {_options.Round.Count} machines; only a host bound to one can follow it";

        // The round is held for the swap, so no cycle reads one address and checks the answer against the other.
        bool wasSequenced = _sequenced;
        BeginSequence();
        _executeGate.Wait();
        try
        {
            _options = _options with { Address = address, Addresses = [] };
            _polling = address;
            _next = 0;
            return null;
        }
        finally
        {
            _executeGate.Release();
            if (!wasSequenced)
                EndSequence();
        }
    }
}
