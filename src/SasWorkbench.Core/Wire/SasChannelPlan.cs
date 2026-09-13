using System.Text;
using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Core.Wire;

/// <summary>
/// How a machine is divided between hosts: a poll address per channel, and the one channel that
/// owns each category of traffic.
/// </summary>
/// <remarks>
/// <para>
/// This is the machine's configuration, not the host's preference, and it is what makes two hosts
/// on one machine safe without either of them policing the other. The platform stores each of the
/// seven categories as a two-bit field holding one channel number, so a category cannot be on two
/// channels — disjointness is structural. It stores a poll address per channel, and a channel
/// whose address is zero is never opened at all.
/// </para>
/// <para>
/// Those two facts together produce the one configuration that has to be refused: a category
/// pointed at a channel with no poll address is owned by nobody, so the traffic it covers reaches
/// no host and the machine answers a poll nothing will ever ask. The machine's own audit menu
/// cannot produce one — it skips a channel with no address while cycling — but a configuration
/// written any other way can, so it is refused here with a reason rather than accepted.
/// </para>
/// <para>
/// It travels with a run because "which host should have sent this poll" and "which host owns this
/// exception" are only answerable if the map is known, and the map lives in the machine.
/// </para>
/// </remarks>
public sealed record SasChannelPlan
{
    private readonly SasAddress?[] _addresses;
    private readonly SasChannel[] _owners;

    private SasChannelPlan(SasAddress?[] addresses, SasChannel[] owners)
    {
        _addresses = addresses;
        _owners = owners;
    }

    /// <summary>Every category, in the order the library's own structure names them.</summary>
    public static IReadOnlyList<SasCategory> Categories { get; } = Enum.GetValues<SasCategory>();

    /// <summary>One machine on one channel, answering at address 01, owning everything.</summary>
    public static SasChannelPlan Single { get; } = Only(SasChannel.One, SasAddress.Of(1));

    /// <summary>A plan with one channel at the given address and every category on it.</summary>
    public static SasChannelPlan Only(SasChannel channel, SasAddress address)
    {
        var addresses = new SasAddress?[SasChannels.Count];
        addresses[channel.ToIndex()] = address;
        SasChannel[] owners = [.. Categories.Select(_ => channel)];
        return new SasChannelPlan(addresses, owners);
    }

    /// <summary>
    /// Builds a plan, refusing one no host could serve.
    /// </summary>
    /// <param name="addresses">
    /// The poll address of each channel, in order, null where the channel has none. The list may
    /// be shorter than four; the channels it does not reach have no address.
    /// </param>
    /// <param name="owners">Which channel owns each category. A category left out stays on channel one.</param>
    /// <param name="plan">The plan, when it is one a machine could hold.</param>
    /// <param name="refusal">Why it is not, when it is not.</param>
    public static bool TryCreate(
        IReadOnlyList<SasAddress?> addresses,
        IReadOnlyDictionary<SasCategory, SasChannel> owners,
        out SasChannelPlan plan,
        out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(owners);

        plan = Single;
        refusal = null;

        var resolved = new SasAddress?[SasChannels.Count];
        for (int index = 0; index < Math.Min(addresses.Count, resolved.Length); index++)
            resolved[index] = addresses[index];

        if (resolved.All(address => address is null))
        {
            refusal = "no channel has a poll address, and a machine with no address on any "
                      + "channel is not running SAS at all";
            return false;
        }

        var assigned = new SasChannel[Categories.Count];
        var unreachable = new List<string>();
        foreach (SasCategory category in Categories)
        {
            SasChannel channel = owners.TryGetValue(category, out SasChannel named)
                ? named
                : SasChannel.One;
            assigned[(int)category] = channel;

            if (resolved[channel.ToIndex()] is null)
            {
                unreachable.Add(
                    $"{SasCategories.NameOf(category)} is on channel {(int)channel}, which has no "
                    + "poll address");
            }
        }

        if (unreachable.Count > 0)
        {
            // Refused rather than quietly moved to channel one. Moving it would make the machine
            // answer a host that was never given that category, which is the opposite of what the
            // configuration asked for, and would hide the mistake.
            refusal = string.Join("; ", unreachable)
                      + ". A category on a channel no host can poll reaches nobody, so this "
                      + "configuration is refused rather than served.";
            return false;
        }

        plan = new SasChannelPlan(resolved, assigned);
        return true;
    }

    /// <summary>The channels that have a poll address, in order.</summary>
    public IReadOnlyList<SasChannel> Open =>
        [.. SasChannels.All.Where(channel => _addresses[channel.ToIndex()] is not null)];

    /// <summary>How many channels a host could open against this machine.</summary>
    public int OpenCount => Open.Count;

    /// <summary>The address a channel answers at, or null when it has none.</summary>
    public SasAddress? AddressOf(SasChannel channel) => _addresses[channel.ToIndex()];

    /// <summary>Which channel owns a category.</summary>
    public SasChannel OwnerOf(SasCategory category) => _owners[(int)category];

    /// <summary>The categories a channel owns, in order.</summary>
    public IReadOnlyList<SasCategory> CategoriesOf(SasChannel channel) =>
        [.. Categories.Where(category => _owners[(int)category] == channel)];

    /// <summary>
    /// Whether a channel would answer a command at all, as far as the category gate decides.
    /// </summary>
    /// <remarks>
    /// A command the library gates on no category is answered on every channel, so this is true
    /// for it. That is not an omission: the cashout ticket information poll is one of them.
    /// </remarks>
    public bool Answers(SasChannel channel, byte command) =>
        !SasCatalogue.CommandCategories.TryGetValue(command, out SasCategory category)
        || _owners[(int)category] == channel;

    /// <summary>Whether an exception reaches a channel's own buffer.</summary>
    public bool Hears(SasChannel channel, byte exception) =>
        !SasCatalogue.ExceptionCategories.TryGetValue(exception, out SasCategory category)
        || _owners[(int)category] == channel;

    /// <summary>
    /// Clears the enabled-feature bits a channel must not report, in place.
    /// </summary>
    /// <remarks>
    /// The features describe the machine and are configured once for it, but the poll that reports
    /// them is answered on a channel — so a channel with no validation must not tell its host that
    /// the machine redeems tickets.
    /// </remarks>
    public void MaskFeatures(SasChannel channel, Span<byte> features)
    {
        foreach (SasCategory category in Categories)
        {
            if (_owners[(int)category] == channel)
                continue;

            if (!SasCatalogue.ChannelFeatureMasks.TryGetValue(category,
                    out IReadOnlyList<SasFeatureMask>? masks))
            {
                continue;
            }

            foreach (SasFeatureMask mask in masks)
            {
                if (mask.Byte < features.Length)
                    features[mask.Byte] &= (byte)~mask.Mask;
            }
        }
    }

    /// <summary>The plan in one line, for a report's provenance.</summary>
    public string Describe()
    {
        var builder = new StringBuilder();
        builder.Append("channels ");
        builder.AppendJoin(' ', Open.Select(c => $"{(int)c}@{_addresses[c.ToIndex()]}"));
        builder.Append(" · ");
        builder.AppendJoin(' ', Categories.Select(
            category => $"{SasCategories.NameOf(category)}={(int)_owners[(int)category]}"));
        return builder.ToString();
    }

    /// <summary>
    /// Reads a whole arrangement written the way <see cref="Describe"/> writes one.
    /// </summary>
    /// <remarks>
    /// Deliberately the same words in and out, so the line a run puts in its provenance can be
    /// pasted straight back in to reproduce the arrangement. The decorations that make it readable
    /// — the leading word and the separator — are ignored rather than required.
    /// </remarks>
    public static bool TryParse(string? text, out SasChannelPlan plan, out string? error)
    {
        plan = Single;
        error = null;

        var addresses = new SasAddress?[SasChannels.Count];
        var owners = new Dictionary<SasCategory, SasChannel>();

        foreach (string word in (text ?? string.Empty)
                 .Split([' ', ',', ';', '	'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (word is "channels" or "·" or "-")
                continue;

            if (word.Contains('@', StringComparison.Ordinal))
            {
                string[] halves = word.Split('@', 2);
                if (!SasChannels.TryParse(halves[0], out SasChannel channel))
                {
                    error = $"'{halves[0]}' is not a channel. Expected 1 to {SasChannels.Count}.";
                    return false;
                }

                if (!SasAddress.TryParse(halves[1], out SasAddress address))
                {
                    error = $"'{halves[1]}' is not a poll address. Expected 00 to 7F.";
                    return false;
                }

                // Zero means the channel has none, the way the machine stores it.
                addresses[channel.ToIndex()] = address.IsSync ? null : address;
                continue;
            }

            if (!TryParseOwners(word, out Dictionary<SasCategory, SasChannel> one, out error))
                return false;

            foreach ((SasCategory category, SasChannel channel) in one)
                owners[category] = channel;
        }

        return TryCreate(addresses, owners, out plan, out error);
    }

    /// <summary>
    /// Reads a plan written the way <see cref="Describe"/> writes the map half:
    /// <c>validation=2 aft=1 …</c>, with addresses given separately.
    /// </summary>
    public static bool TryParseOwners(
        string? text, out Dictionary<SasCategory, SasChannel> owners, out string? error)
    {
        owners = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (string pair in text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] halves = pair.Split('=', 2);
            if (halves.Length != 2)
            {
                error = $"'{pair}' is not a category and a channel. Expected something like "
                        + "validation=2";
                return false;
            }

            if (!SasCategories.TryParse(halves[0], out SasCategory category))
            {
                error = $"'{halves[0]}' is not a category. Known: {SasCategories.Names}.";
                return false;
            }

            if (!SasChannels.TryParse(halves[1], out SasChannel channel))
            {
                error = $"'{halves[1]}' is not a channel. Expected 1 to {SasChannels.Count}.";
                return false;
            }

            owners[category] = channel;
        }

        return true;
    }

    public override string ToString() => Describe();
}

/// <summary>How a category is written, so a script, a report and the command line agree.</summary>
public static class SasCategories
{
    /// <summary>The library's own name for a category, underscored.</summary>
    public static string NameOf(SasCategory category) =>
        SasNames.Underscore(category.ToString()).ToLowerInvariant();

    /// <summary>Every name, for a message that has to list them.</summary>
    public static string Names =>
        string.Join(", ", Enum.GetValues<SasCategory>().Select(NameOf));

    public static bool TryParse(string? text, out SasCategory category)
    {
        category = SasCategory.GeneralControl;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string wanted = text.Trim().Replace("-", "_");
        foreach (SasCategory candidate in Enum.GetValues<SasCategory>())
        {
            if (string.Equals(NameOf(candidate), wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        return false;
    }
}
