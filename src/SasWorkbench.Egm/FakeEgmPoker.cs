using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// A declared draw poker game (D61): the hand it registers with the library for $8E, the cards held and released
/// ($8B) and the draw request ($87), each an event the library sends only while reporting is on.
/// </summary>
/// <remarks>
/// The library stores whatever hand the game registers and answers $8E with it (libsas.cpp:1345, lp.cpp:8838), and
/// queues $8B with the byte the game gives (libsas.cpp:1750). It has no call for $87, and neither has the platform's
/// game interface (QSP libqsp.h:366), so the draw is raised through the library's plain exception write, as the GSA
/// document asks of a poker machine. The deal is fixed, so a case can name the cards.
/// </remarks>
public sealed partial class FakeEgm
{
    /// <summary>The hand dealt at every game's start: ace of spades, king of clubs, nine of hearts, five of diamonds, seven of spades.</summary>
    public static IReadOnlyList<byte> DealtHand { get; } = [0x0C, 0x1B, 0x27, 0x33, 0x05];

    /// <summary>The cards drawn, in turn, for each card not held.</summary>
    public static IReadOnlyList<byte> DrawnCards { get; } = [0x2C, 0x3C, 0x1C, 0x0B, 0x2B];

    private const byte HandDealt = 0x00;
    private const byte HandFinal = 0x01;
    private const byte CardHeldBit = 0x80;

    private readonly byte[] _hand = new byte[5];
    private readonly bool[] _held = new bool[5];
    private byte _handType;
    private bool _drawn;

    /// <summary>A game started: the five cards dealt and registered. Called under the gate.</summary>
    private void DealLocked()
    {
        if (!_options.Poker)
            return;

        for (int i = 0; i < _hand.Length; i++)
            _hand[i] = DealtHand[i];
        Array.Clear(_held);
        _handType = HandDealt;
        _drawn = false;
    }

    /// <summary>The draw: each card not held replaced, and the hand registered as final. Called under the gate.</summary>
    private void DrawLocked()
    {
        if (!_options.Poker || _drawn)
            return;

        int next = 0;
        for (int i = 0; i < _hand.Length; i++)
        {
            if (!_held[i])
                _hand[i] = DrawnCards[next++];
        }

        _handType = HandFinal;
        _drawn = true;
    }

    /// <summary>The hand $8E reports: zeros until a game has been dealt, as the library starts its record (libsas_internal.cpp:365).</summary>
    private Dictionary<string, object> CardInformation()
    {
        lock (_gate)
        {
            return new Dictionary<string, object>
            {
                ["hand_type"] = _handType,
                ["card_1"] = _hand[0],
                ["card_2"] = _hand[1],
                ["card_3"] = _hand[2],
                ["card_4"] = _hand[3],
                ["card_5"] = _hand[4],
            };
        }
    }

    /// <summary>The player holds or releases one card, numbered from 1 on the left, while a dealt hand awaits its draw.</summary>
    /// <returns>Why the machine cannot do it, or null.</returns>
    public string? HoldCard(int position, bool held)
    {
        if (!_options.Poker)
            return "the machine has no poker game";
        if (position is < 1 or > 5)
            return "a hand has five cards, numbered 1 to 5 from the left";

        lock (_gate)
        {
            if (!_gameInProgress || _drawn)
                return "no dealt hand is waiting for its draw; start a game first";
            _held[position - 1] = held;
        }

        RaiseEvent((byte)SasExceptionCode.CardHeldOrNotHeld, (byte)((held ? CardHeldBit : 0) | (position - 1)));
        return null;
    }

    /// <summary>The player asks to draw: $87, and the cards not held replaced.</summary>
    /// <returns>Why the machine cannot do it, or null.</returns>
    public string? DrawCards()
    {
        if (!_options.Poker)
            return "the machine has no poker game";

        lock (_gate)
        {
            if (!_gameInProgress || _drawn)
                return "no dealt hand is waiting for its draw; start a game first";
            DrawLocked();
        }

        RaiseEvent((byte)SasExceptionCode.PlayerRequestedDrawCards);
        return null;
    }
}
