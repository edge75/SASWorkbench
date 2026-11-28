namespace SasWorkbench.Qpm.Tests;

/// <summary>
/// Reading a log the demo is still appending to. A count taken before a key is posted must index exactly the first line
/// written after it: counting the empty piece after the last newline made the F10 registration step skip the very line
/// that proved the key arrived, and then report that the key never reached the game.
/// </summary>
public class LogLinesTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a\n", 1)]
    [InlineData("a\nb\n", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("a\nhalf-writ", 1)]
    public void OnlyLinesEndedByANewlineAreCounted(string text, int count) =>
        Assert.Equal(count, LogLines.Complete(text).Length);

    [Fact]
    public void TheCarriageReturnIsNotPartOfTheLine() =>
        Assert.Equal(["a", "b"], LogLines.Complete("a\r\nb\r\n"));

    [Fact]
    public void ACountTakenNowIndexesTheNextLineWritten()
    {
        string before = "one\ntwo\n";
        string after = before + "three\n";

        int count = LogLines.Complete(before).Length;

        Assert.Equal("three", LogLines.Complete(after).Skip(count).First());
    }

    [Fact]
    public void TheDemosFirstLogLineAfterTheCountIsFound()
    {
        using var directory = new TemporaryDirectory();
        var demo = new DemoProcess(Path.Combine(directory.Path, "OpenGL_Game.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(demo.LogPath)!);
        File.WriteAllText(demo.LogPath, "[GameManager] started\r\nAFT registration: old line, status now 0x00\r\n");

        int before = demo.LogLineCount();
        File.AppendAllText(demo.LogPath, "AFT registration: request accepted, status now 0x40\r\n");

        Assert.Equal(2, before);
        Assert.Equal("AFT registration: request accepted, status now 0x40",
            demo.WaitForLogLine(before, "AFT registration:", TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ADemoLogThatDoesNotExistHasNoLines()
    {
        using var directory = new TemporaryDirectory();
        var demo = new DemoProcess(Path.Combine(directory.Path, "OpenGL_Game.exe"));

        Assert.Equal(0, demo.LogLineCount());
        Assert.Null(demo.WaitForLogLine(0, "anything", TimeSpan.Zero));
    }

    [Fact]
    public void TheDrawLogsFirstLineAfterTheCountIsRead()
    {
        using var directory = new TemporaryDirectory();
        var outcomes = new ScriptedOutcomes(directory.Path);
        string log = Path.Combine(directory.Path, "draws.log");
        File.WriteAllText(log, "1 done h1-1 slot\n");

        int before = outcomes.LogLineCount();
        File.AppendAllText(log, "2 refused h1-2: no alternative\n");

        Assert.Equal(1, before);
        Assert.Contains("refused", outcomes.WaitForDrawn("h1-2", before, TimeSpan.FromSeconds(1)), StringComparison.Ordinal);
    }
}
