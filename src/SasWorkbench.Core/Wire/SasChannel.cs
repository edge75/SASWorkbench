namespace SasWorkbench.Core.Wire;

/// <summary>
/// A SAS channel. A gaming machine supports four independent links, each with its own
/// exception buffer and link state.
/// </summary>
/// <remarks>
/// Carried through the model, the script syntax and the reports from the outset even while
/// only one channel is wired: retrofitting a channel dimension into a poll engine built for
/// one is far more painful than never assuming one.
/// </remarks>
public enum SasChannel
{
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
}

/// <summary>Helpers for <see cref="SasChannel"/>.</summary>
public static class SasChannels
{
    /// <summary>How many channels a gaming machine can serve at once.</summary>
    public const int Count = 4;

    /// <summary>Every channel, in order.</summary>
    public static readonly SasChannel[] All =
        [SasChannel.One, SasChannel.Two, SasChannel.Three, SasChannel.Four];

    /// <summary>A zero-based index, for array storage.</summary>
    public static int ToIndex(this SasChannel channel) => (int)channel - 1;

    public static bool TryParse(string? text, out SasChannel channel)
    {
        channel = SasChannel.One;
        if (!int.TryParse(text, out int value) || value is < 1 or > Count)
            return false;

        channel = (SasChannel)value;
        return true;
    }
}
