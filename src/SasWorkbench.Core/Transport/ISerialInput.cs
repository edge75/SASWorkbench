namespace SasWorkbench.Core.Transport;

/// <summary>The receiving side of a serial port, as the reader uses it.</summary>
public interface ISerialInput
{
    /// <summary>How many received bytes are waiting to be read.</summary>
    int BytesToRead { get; }

    /// <summary>Reads up to <paramref name="count"/> waiting bytes.</summary>
    int Read(byte[] buffer, int offset, int count);
}
