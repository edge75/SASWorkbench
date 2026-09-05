using System.Reflection;
using SasWorkbench.Core.Reporting;

namespace SasWorkbench.Cli;

/// <summary>
/// The help text, written so that somebody can use this tool without a manual.
/// </summary>
internal static class Help
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static void Print(TextWriter? writer = null)
    {
        TextWriter output = writer ?? Console.Out;
        output.WriteLine($"""
            sasctl {Version} — a scriptable SAS host for testing gaming machine software

            USAGE
              sasctl run <path> [options]        run a script, or every script under a directory
              sasctl probe [options]             report what this environment can and cannot do
              sasctl catalogue [what]            list the protocol this build knows
              sasctl egm [options]               run the software gaming machine as a peer
              sasctl qpm <action> [options]      drive a running machine's peripherals by hand
              sasctl nvram <action> [options]    save or restore a machine's configured memory
              sasctl find <file> [options]       find messages in a recorded run, by line number

            RUN
              --tcp <host:port>       reach the machine over TCP, overriding what the script says
              --serial <port>         reach it over a serial port, such as COM12
              --loopback              run against the software machine in this process
              --address <hh>          poll address, in hex, overriding the script
              --channel <1-4>         which channel to drive
              --poll <duration>       poll interval, such as 200ms
              --stimulus <backend>    none, manual, software or qpm  (default: none)
              --ask-for <duration>    how long a manual step waits for a person before it
                                      is skipped  (default: 2m)
              --egm-resources <dir>   the machine's own resource directory, which is where it
                                      declares its doors and buttons  (--stimulus qpm)
              --qpm-shm <file>        the peripheral shared region, if it is not in the usual
                                      place for this platform  (--stimulus qpm)
              --report <formats>      {Reports.Names}  (comma-separated)
              --out <directory>       where to write reports  (default: runs/<timestamp>)
              --tag <name>            run only tests carrying this label
              --skip-tag <name>       skip tests carrying this label
              --list                  list the tests that would run, and stop
              --quiet                 print only the summary

            EXIT CODES
              0  every test that ran passed
              1  at least one test failed
              2  the tool could not do what was asked
              3  nothing was actually tested — every test was skipped

              A run in which everything was skipped is deliberately not a success. A suite that
              could not test anything must not look like one that tested everything.

            CATALOGUE
              sasctl catalogue                   a count of each kind
              sasctl catalogue long-polls        every long poll the machine library implements
              sasctl catalogue exceptions        every exception a machine can report
              sasctl catalogue meters            every meter
              sasctl catalogue layouts           the replies this build can decode, field by field

            EGM
              --tcp <host:port>       listen for a host, or dial one with --dial
              --dial                  dial the host instead of waiting for it
              --address <hh>          the address this machine answers to  (default: 01)

              Both ends of a run against this peer are our own code, so a pass says the two agree
              and nothing more. It is a fixture for fast tests, not the thing under test.

            QPM
              status                  what the machine is, and which lamps it has lit
              settle                  shut every door and reconnect the note acceptor
              audit open|close        open the machine's configuration menu, or close it. The
                                      machine needs its main and logic doors open before it will
                                      let anything be configured, so this does those too.
              door <name> open|close  open or shut one door
              press <button>          press a button and let go of it
              hold <button> on|off    turn a keyswitch, and leave it turned
              bill <cents>            offer a note to the acceptor

              Takes --egm-resources and --qpm-shm, as 'run' does. The machine must be running:
              nothing written here is seen by a machine that is not reading it, so a machine
              whose heartbeat has stopped is refused rather than written to.

            FIND
              --lp <hh>               a long poll, in either direction: the command and its reply
              --exception <hh>        an exception the machine reported, by code or by name
              --poll sync|general|long   polls of one kind
              --side host|egm|both    whose messages to look at  (default: both)
              --text <text>           a message whose symbols or note contain this
              --address <hh>          the machine's address, which tells an acknowledgement from
                                      an exception of the same value

              Reads a run written with --report jsonl and prints every match with the line it is
              on, so the file already open in an editor can be jumped into rather than replaced by
              a filtered copy of itself.

            NVRAM
              status                  what the machine holds, and what has been saved
              save                    save the machine's memory as a fixture
              restore                 write a saved fixture back over it, keeping what was there

              --fixture <file>        which saved memory  (default: fixtures/nvram/golden.nvram.gz)
              --nvram <file>          the machine's memory, if it is not in the usual place

              A machine's note acceptor, its printer and which notes it takes live in this memory
              and are set through its own menu, by hand. Both actions refuse while a machine is
              running, because a copy taken or written under one is a configuration nobody had.

            EXAMPLES
              sasctl run tests/regression --loopback --stimulus software
              sasctl run tests/conformance/10.1 --tcp 127.0.0.1:7200 --report html,junit
              sasctl probe --serial COM12
              sasctl probe --stimulus qpm --egm-resources <demo>/Resources
              sasctl egm --tcp 127.0.0.1:7200
              sasctl qpm status --egm-resources <demo>/Resources
              sasctl qpm audit open --egm-resources <demo>/Resources
              sasctl nvram save
            """);
    }
}
