using SasWorkbench.Core.Polling;
using SasWorkbench.Core.Wire;

namespace SasWorkbench.Core.Scripting;

/// <summary>What a stimulus backend may read from the host while it carries out a step, to confirm what it did.</summary>
/// <remarks>
/// A backend that presses a button on a real machine cannot tell from the button whether anything happened. This lets
/// it check on the wire - a game ended, a status moved - instead of trusting the press. Nothing here takes an exception
/// off the queue, so the case's own assertions still see everything.
/// </remarks>
public interface IHostView
{
    /// <summary>Null when the host can be asked, or why it cannot: a sequenced case draws every reply itself.</summary>
    string? Unavailable { get; }

    /// <summary>A point among the exceptions heard so far, for <see cref="ExceptionAfter"/>.</summary>
    long Mark();

    /// <summary>Waits for an exception of that code heard after the mark, without consuming it; null when none came.</summary>
    ExceptionReport? ExceptionAfter(byte code, long mark, TimeSpan within);

    /// <summary>Sends one long poll and returns what came back; a general poll is let through before the next one.</summary>
    LongPollResult Ask(byte command, byte[] data);
}

/// <summary>A backend that can say before a case starts which of its steps it would refuse.</summary>
/// <remarks>The case is then skipped before it inserts a note or opens a door, rather than part-way with the machine changed.</remarks>
public interface IStimulusPreflight
{
    /// <summary>Why this step would be refused whatever the machine's state, or null when it may be carried.</summary>
    string? Refusal(StimulusStep step);
}

/// <summary>A backend that confirms some steps through the host.</summary>
public interface IHostAwareStimulusBackend : IStimulusBackend
{
    /// <summary>Carries out one step, reading the host where it needs to confirm it.</summary>
    /// <exception cref="StimulusUnavailableException">This backend cannot do it, or could not confirm it.</exception>
    void Apply(StimulusStep step, SasAddress? machine, IHostView host);
}
