namespace SasWorkbench.Core.Scripting;

/// <summary>The format an exception drawn by a poll a case placed must come in.</summary>
public enum DrawFormat
{
    /// <summary>Either: the case asserts only the code.</summary>
    Any,

    /// <summary>The real-time event format: address, marker, code, payload and check sequence.</summary>
    Event,

    /// <summary>The single byte a general poll carries with event reporting off.</summary>
    Bare,
}

/// <summary>
/// Sends one long poll in a sequence and expects it answered with a real-time event instead of its reply.
/// </summary>
/// <remarks>
/// A machine reporting real-time events answers an addressed long poll with the oldest queued event
/// (D43), so which exception a long poll drew is a property of that one poll, as it is for a general
/// poll. A repeat of the same poll is an implied NACK, and the third may go unanswered, which
/// <see cref="OrSilence"/> admits.
/// </remarks>
public sealed record SendLongPollDrawsEventStep(
    SourceLocation Location, SendLongPollStep Poll, byte Code, string CodeName, bool OrSilence)
    : ScriptStep(Location)
{
    public override string Describe() =>
        $"{Poll.Describe()} draws event {CodeName}{(OrSilence ? " or silence" : string.Empty)}";
}
