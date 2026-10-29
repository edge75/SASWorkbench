using System.IO.Ports;

namespace SasWorkbench.Core.Transport;

/// <summary>The operations of a serial port that the wakeup-bit timing depends on.</summary>
public interface ISerialLine
{
    /// <summary>The parity applied to the next byte written.</summary>
    Parity Parity { get; set; }

    /// <summary>Returns once every byte written so far has left the wire.</summary>
    void Drain();

    void WriteByte(byte value);

    void Flush();
}
