using SasWorkbench.Core.Protocol;

namespace SasWorkbench.Egm;

/// <summary>
/// Registration for funds transfer: long poll $73, the attendant's half of it, and the three
/// exceptions $6C, $6D and $6E.
/// </summary>
/// <remarks>Follows lp73_RegisterGamingMachine (lp.cpp:6097) branch for branch, and is never persisted.</remarks>
public sealed partial class FakeEgm
{
    private const byte RegistrationReady = 0x00;
    private const byte Registered = 0x01;
    private const byte RegistrationPending = 0x40;
    private const byte NotRegistered = 0x80;

    private const byte InitializeRegistration = 0x00;
    private const byte RegisterMachine = 0x01;
    private const byte RequestAcknowledgement = 0x40;
    private const byte Unregister = 0x80;
    private const byte ReadRegistration = 0xFF;

    private byte _registrationStatus = NotRegistered;
    private byte[] _registrationKey = new byte[20];
    private uint _posId;

    /// <summary>The registration status $73 reports: $00 ready, $01 registered, $40 pending, $80 not registered.</summary>
    public byte AftRegistrationStatus
    {
        get
        {
            lock (_gate)
                return _registrationStatus;
        }
    }

    /// <summary>
    /// The attendant's register action: asks the host to register when the machine is not
    /// registered, and acknowledges a registration the host is waiting on.
    /// </summary>
    /// <returns>Null when the action was taken, or why the menu refuses it.</returns>
    public string? AttendantRegister()
    {
        lock (_gate)
        {
            // QSP DecideAftRegistrationAction: the platform refuses rather than pretend the library acted.
            if (!_options.Aft)
                return "the machine performs no funds transfer, so it has no registration to start";
            if (AssetNumber == 0)
                return "the machine has no asset number, and a machine without one may not register";

            switch (_registrationStatus)
            {
                case NotRegistered:
                    // Only a request: the machine stays unregistered until the host initialises (libsas_wrapper.cpp:3097).
                    QueueRanked((byte)SasExceptionCode.AFTRequestToRegister);
                    return null;

                case RegistrationPending:
                    _registrationStatus = RegistrationReady;
                    QueueRanked((byte)SasExceptionCode.AFTRegistrationAcknowledged);
                    return null;

                default:
                    return _registrationStatus == Registered
                        ? "the machine is already registered, so the menu offers nothing to register"
                        : "a registration is already under way and waits for the host, not the attendant";
            }
        }
    }

    /// <summary>What long poll $73 answers, or null for the NACK the library sends (lp.cpp:6146, :6156).</summary>
    private Dictionary<string, object>? RegisterGamingMachine(ReadOnlySpan<byte> request)
    {
        if (request.Length < 4)
            return null;

        byte code = request[3];
        if (code is not (InitializeRegistration or RegisterMachine or RequestAcknowledgement or Unregister
            or ReadRegistration))
        {
            return null;
        }

        bool carriesData = code is InitializeRegistration or RegisterMachine or RequestAcknowledgement;
        if (carriesData && (request[2] < 0x1D || request.Length < 32))
            return null;

        // Only a well-formed poll reaches the AFT check, which then ignores it (lp.cpp:6173).
        if (!_options.Aft)
            return NoAnswer;

        uint asset = 0;
        uint pos = 0;
        bool keyZero = true;
        if (carriesData)
        {
            asset = LittleEndian(request.Slice(4, 4));
            pos = LittleEndian(request.Slice(28, 4));
            keyZero = !request.Slice(8, 20).ContainsAnyExcept((byte)0);
        }

        byte[] key = carriesData ? request.Slice(8, 20).ToArray() : [];

        lock (_gate)
        {
            uint own = AssetNumber;
            if (code != ReadRegistration && (asset != own || own == 0))
            {
                // Another asset number, or none, undoes whatever registration there was (lp.cpp:6207).
                CancelRegistration();
            }
            else if (code == InitializeRegistration)
            {
                // A zero key or point of sale clears the stored one; a non-zero one is not stored yet (lp.cpp:6255).
                _registrationStatus = RegistrationReady;
                if (keyZero)
                    _registrationKey = new byte[20];
                if (pos == 0)
                    _posId = 0;
            }
            else if (code == RegisterMachine && _registrationStatus == RegistrationReady && !keyZero)
            {
                _registrationKey = key;
                _posId = pos;
                _registrationStatus = Registered;
            }
            else if (code == RequestAcknowledgement && _registrationStatus == RegistrationReady)
            {
                _registrationStatus = RegistrationPending;
            }
            else if (code != ReadRegistration)
            {
                // Cancel, or a step out of order: not registered (lp.cpp:6328).
                CancelRegistration();
            }

            return new Dictionary<string, object>
            {
                ["length"] = 0x1D,
                ["registration_status"] = _registrationStatus,
                ["asset_number"] = (long)own,
                ["registration_key"] = _registrationKey.ToArray(),
                ["pos_id"] = (long)_posId,
            };
        }
    }

    /// <summary>Not registered, with $6E when that is a change (section 8.1 as the library raises it).</summary>
    private void CancelRegistration()
    {
        if (_registrationStatus == NotRegistered)
            return;

        _registrationStatus = NotRegistered;
        QueueOrdinary((byte)SasExceptionCode.AFTRegistrationCancelled);
    }

    /// <summary>A loop break on the AFT channel throws away a registration that was started and not finished (libsas_threads.cpp:2519).</summary>
    private void CancelUnfinishedRegistration()
    {
        lock (_gate)
        {
            if (_registrationStatus is RegistrationReady or RegistrationPending)
                CancelRegistration();
        }
    }

    /// <summary>
    /// Turns AFT bonusing on or off, as the operator would in the machine's menu.
    /// </summary>
    /// <remarks>Changing the AFT configuration cancels any registration, as QSP's AftConfigurationChanged has it.</remarks>
    public void SetAftBonus(bool on)
    {
        lock (_gate)
        {
            if (_aftBonus == on)
                return;

            _aftBonus = on;
            CancelRegistration();
        }
    }

    /// <summary>The key a debit transfer is checked against, as internalAftRegistrationStatusForKey checks it.</summary>
    private byte RegistrationRefusal(bool debit, byte[] key)
    {
        if (!debit)
            return 0;
        if (_registrationStatus != Registered)
            return 0x88;
        if (!key.AsSpan().ContainsAnyExcept((byte)0) || !key.AsSpan().SequenceEqual(_registrationKey))
            return 0x89;
        return _posId == 0 ? (byte)0x8A : (byte)0;
    }

    private static uint LittleEndian(ReadOnlySpan<byte> bytes) =>
        (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
}
