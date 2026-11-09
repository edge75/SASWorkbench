using System.Collections.Concurrent;

namespace SasWorkbench.Core.Scripting;

/// <summary>
/// Asks a person at a text console, giving up at the deadline.
/// </summary>
/// <remarks>
/// One thread reads the input for the whole run and queues the lines, so a prompt that ran out of
/// time leaves no read behind to swallow the answer meant for the next one. Lines typed while no
/// prompt is open are dropped when the next prompt opens, so a stray Enter is not taken as "no".
/// </remarks>
public sealed class LinePrompt(TextReader input, TextWriter output)
{
    private readonly TextReader _input = input ?? throw new ArgumentNullException(nameof(input));
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly BlockingCollection<string> _lines = new();
    private readonly object _gate = new();
    private bool _reading;

    /// <summary>Asks, and returns what the person said or that nobody did.</summary>
    public PromptAnswer Ask(string request, TimeSpan deadline)
    {
        StartReading();

        while (_lines.TryTake(out _))
        {
        }

        _output.WriteLine();
        _output.WriteLine($"  {request}");
        _output.Write($"  done? [y/N] (giving up in {deadline.TotalSeconds:F0}s) ");
        _output.Flush();

        if (!_lines.TryTake(out string? line, deadline))
        {
            // Closed input ends the wait early; it declines, as a closed console read always has.
            if (_lines.IsCompleted)
                return PromptAnswer.Declined;

            _output.WriteLine();
            _output.WriteLine("  nobody answered.");
            return PromptAnswer.NoAnswer;
        }

        return line.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)
            ? PromptAnswer.Done
            : PromptAnswer.Declined;
    }

    // Started on the first prompt, so a run that never asks never takes the console's input.
    private void StartReading()
    {
        lock (_gate)
        {
            if (_reading)
                return;
            _reading = true;
        }

        var reader = new Thread(ReadLines) { IsBackground = true, Name = "manual prompt input" };
        reader.Start();
    }

    private void ReadLines()
    {
        try
        {
            while (_input.ReadLine() is { } line)
                _lines.Add(line);
        }
        catch (IOException)
        {
            // A console that goes away ends the input like end of file.
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _lines.CompleteAdding();
        }
    }
}
