using SasWorkbench.Core.Wire;

namespace SasWorkbench.Egm;

/// <summary>
/// Reading a transfer back: the interrogation form of long poll $72, the history position it
/// reports, and the bytes of the reply.
/// </summary>
/// <remarks>
/// The position arithmetic is the library's, branch for branch (lp.cpp:5746 to 5875), including the
/// relative positions $81 to $FF count back from the newest entry.
/// </remarks>
public sealed partial class FakeEgm
{
    private (int Position, AftRecord Response) Interrogate(byte code, byte index)
    {
        if (!_options.Aft && _aftCycle == AftCycle.Completed)
            return (index, new AftRecord { TransferStatus = 0x82 });

        int position = InterrogatedPosition(code, index);

        // Reading the current transfer once it is final closes the cycle and stops $69 (lp.cpp:5889).
        if (_aftCycle == AftCycle.Updated && code == 0xFF && index == 0)
        {
            _aftCycle = AftCycle.Completed;
            if (_zeroAmount == AftZeroAmount.Pending)
            {
                _zeroAmount = AftZeroAmount.Completed;
            }
            else if (_history[_aftIndex].TransferStatus is 0x00 or 0x01)
            {
                _aftIndex++;
                if (_aftIndex == HistoryDepth)
                {
                    _aftIndex = 1;
                    _aftOverflow = true;
                }
            }

            _completionReissueDue = 0;
            Withdraw((byte)Core.Protocol.SasExceptionCode.AFTTransferComplete);
        }

        if (_aftCycle == AftCycle.None || position == 0xFF || position >= HistoryDepth)
            return (position, new AftRecord { TransferStatus = 0xFF, ReceiptStatus = NoReceipt });

        AftRecord stored = _history[position];
        AftRecord response = stored.Copy();

        // The flags are replayed from the transfer as it happened, the request's own bits over its stored reply (lp.cpp:5954).
        response.ResponseFlags = (byte)((stored.ResponseFlags & ~0xE8) | (stored.RequestFlags & 0xE8) & ~0x10);
        return (position, response);
    }

    private int InterrogatedPosition(byte code, byte index)
    {
        bool relative = index > HistoryDepth;
        int relativePosition = relative ? 0x7F - (index & 0x7F) + 1 : 0;
        bool pending = _aftCycle is AftCycle.Pending or AftCycle.PendingPartial or AftCycle.Updated;

        if (_zeroAmount == AftZeroAmount.Pending)
            return 0;
        if (_history[0].TransferStatus >= 0x80 && index == 0)
            return 0;
        if (index == 0 && _zeroAmount == AftZeroAmount.Completed)
            return 0;

        if ((!relative
                && ((index >= _aftIndex && _aftCycle == AftCycle.Completed) || (index > _aftIndex && pending))
                && !_aftOverflow)
            || (relative && relativePosition >= _aftIndex && !_aftOverflow))
        {
            return 0xFF;
        }

        if (_aftCycle is AftCycle.Completed or AftCycle.Pending
            && ((!_aftOverflow && _aftIndex > 1) || (_aftOverflow && _aftIndex >= 1)))
        {
            if (!relative)
                return index != 0 ? index : _aftCycle == AftCycle.Pending ? 0 : _aftIndex - 1;

            return relativePosition >= _aftIndex && _aftOverflow
                ? 0x7F - (relativePosition - _aftIndex)
                : _aftIndex - relativePosition;
        }

        if (_aftCycle == AftCycle.Updated && code is 0xFE or 0xFF)
        {
            if (!relative)
                return index != 0 ? index : _aftIndex;

            return relativePosition > _aftIndex && _aftOverflow
                ? 0x7F - (relativePosition - _aftIndex - 1)
                : _aftIndex - relativePosition + 1;
        }

        if (_aftCycle == AftCycle.None)
            return 0;

        // Pending with no history yet keeps the position the host asked for; anything else reads the next slot.
        return _aftCycle == AftCycle.Pending ? index : _aftIndex;
    }

    /// <summary>The reply as lp72_TransferFunds lays it out (lp.cpp:5967 to 6044).</summary>
    private byte[] Compose(byte address, int position, AftRecord record, bool interrogation)
    {
        var body = new List<byte> { address, 0x72, 0x00 };
        byte status = record.TransferStatus;

        // The library never writes the position byte of a $C0 or $C1 answer; zero is sent here (D45).
        if (status is 0xC0 or 0xC1)
        {
            body.Add(0x00);
            body.Add(status);
            return Seal(body);
        }

        if (interrogation && (_aftCycle == AftCycle.None || position == 0xFF))
        {
            body.AddRange([0x00, status, record.ReceiptStatus]);
            return Seal(body);
        }

        body.Add((byte)position);
        body.Add(status);
        body.Add(record.ReceiptStatus);
        body.Add(record.TransferType);

        (long cash, long restricted, long nonrestricted) = status == Pending
            ? (record.RequestedCashable, record.RequestedRestricted, record.RequestedNonrestricted)
            : status <= 0x80
                ? (record.Cashable, record.Restricted, record.Nonrestricted)
                : (0L, 0L, 0L);
        body.AddRange(BcdBytes(cash, 5));
        body.AddRange(BcdBytes(restricted, 5));
        body.AddRange(BcdBytes(nonrestricted, 5));
        body.Add(record.ResponseFlags);

        uint asset = AssetNumber;
        body.AddRange([(byte)asset, (byte)(asset >> 8), (byte)(asset >> 16), (byte)(asset >> 24)]);
        body.Add((byte)record.TransactionId.Length);
        body.AddRange(record.TransactionId);

        if (record.CompletedAt is { } at)
        {
            body.AddRange(BcdBytes(at.Month, 1));
            body.AddRange(BcdBytes(at.Day, 1));
            body.AddRange(BcdBytes(at.Year, 2));
            body.AddRange(BcdBytes(at.Hour, 1));
            body.AddRange(BcdBytes(at.Minute, 1));
            body.AddRange(BcdBytes(at.Second, 1));
        }
        else
        {
            body.AddRange(new byte[7]);
        }

        body.AddRange(record.Expiration);
        body.AddRange([(byte)record.PoolId, (byte)(record.PoolId >> 8)]);
        foreach (AftMeter meter in (AftMeter[])[record.CashableMeter, record.RestrictedMeter, record.NonrestrictedMeter])
        {
            body.Add((byte)meter.Size);
            body.AddRange(BcdBytes(meter.Value, meter.Size));
        }

        return Seal(body);
    }

    private static byte[] Seal(List<byte> body)
    {
        body[2] = (byte)(body.Count - 3);
        ushort crc = SasCrc.Compute([.. body]);
        body.Add(SasCrc.LowByte(crc));
        body.Add(SasCrc.HighByte(crc));
        return [.. body];
    }
}
