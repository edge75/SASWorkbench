using System.Collections.Concurrent;
using SasWorkbench.Core.Scripting;

namespace SasWorkbench.Core.Tests;

/// <summary>
/// The console prompt behind <c>--stimulus manual</c>. The case that mattered on the board: once
/// one prompt ran out of time, the read it had started kept waiting and swallowed the person's
/// next answer, so every later prompt also ran out of time whatever was typed. These tests pin
/// that an answer always reaches the prompt that is open when it is typed.
/// </summary>
public class LinePromptTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnswerAfterATimedOutPromptReachesTheNextPrompt()
    {
        var input = new TypedLines();
        var output = new PromptOutput();
        var prompt = new LinePrompt(input, output);

        Assert.Equal(PromptAnswer.NoAnswer, prompt.Ask("first", TimeSpan.FromMilliseconds(50)));

        Task<PromptAnswer> second = Task.Run(() => prompt.Ask("second", Long), Cancel);
        output.WaitFor("second");
        input.Type("y");

        Assert.Equal(PromptAnswer.Done, await second.WaitAsync(Long, Cancel));
    }

    [Fact]
    public async Task ALineTypedWhileNoPromptIsOpenIsNotTakenAsTheNextAnswer()
    {
        var input = new TypedLines();
        var output = new PromptOutput();
        var prompt = new LinePrompt(input, output);

        Task<PromptAnswer> first = Task.Run(() => prompt.Ask("first", Long), Cancel);
        output.WaitFor("first");
        input.Type("y");
        Assert.Equal(PromptAnswer.Done, await first.WaitAsync(Long, Cancel));

        // A stray Enter between prompts would otherwise read as "no" to the next question.
        input.Type("");
        input.WaitUntilRead(2);

        Assert.Equal(PromptAnswer.NoAnswer, prompt.Ask("second", TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task AnythingButYesIsADecline()
    {
        var input = new TypedLines();
        var output = new PromptOutput();
        var prompt = new LinePrompt(input, output);

        Task<PromptAnswer> asked = Task.Run(() => prompt.Ask("question", Long), Cancel);
        output.WaitFor("question");
        input.Type("n");

        Assert.Equal(PromptAnswer.Declined, await asked.WaitAsync(Long, Cancel));
    }

    [Fact]
    public async Task ClosedInputDeclinesAtOnceRatherThanWaitingOutTheDeadline()
    {
        var prompt = new LinePrompt(new StringReader(""), new PromptOutput());

        Task<PromptAnswer> asked = Task.Run(() => prompt.Ask("question", Long), Cancel);

        // Well inside the ten-second deadline: closed input must not wait it out.
        Assert.Equal(PromptAnswer.Declined, await asked.WaitAsync(TimeSpan.FromSeconds(5), Cancel));
    }

    /// <summary>A console the test types into, one line at a time.</summary>
    private sealed class TypedLines : TextReader
    {
        private readonly BlockingCollection<string> _lines = new();
        private int _read;

        public void Type(string line) => _lines.Add(line);

        public void WaitUntilRead(int count)
        {
            DateTime until = DateTime.UtcNow + Long;
            while (Volatile.Read(ref _read) < count)
            {
                Assert.True(DateTime.UtcNow < until, $"only {_read} of {count} lines were read");
                Thread.Sleep(5);
            }
        }

        public override string? ReadLine()
        {
            string line = _lines.Take();
            Interlocked.Increment(ref _read);
            return line;
        }
    }

    /// <summary>What the prompt printed, safe to watch from the test thread.</summary>
    private sealed class PromptOutput : StringWriter
    {
        private readonly object _gate = new();

        public override void Write(char value)
        {
            lock (_gate)
                base.Write(value);
        }

        public override void Write(string? value)
        {
            lock (_gate)
                base.Write(value);
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
                base.WriteLine(value);
        }

        public void WaitFor(string text)
        {
            DateTime until = DateTime.UtcNow + Long;
            while (true)
            {
                lock (_gate)
                {
                    if (ToString().Contains(text, StringComparison.Ordinal))
                        return;
                }

                Assert.True(DateTime.UtcNow < until, $"the prompt never printed '{text}'");
                Thread.Sleep(5);
            }
        }
    }
}
