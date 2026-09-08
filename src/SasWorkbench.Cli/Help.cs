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
              sasctl explain <file> [options]    explain a recorded run, packet by packet
              sasctl import <path>... [options]  read traffic another tool recorded
              sasctl sweep [options]             ask a machine every command it may be asked
              sasctl events [options]            make a machine report in real time, and read
                                                 the payloads its exceptions carry

            RUN
              --tcp <host:port>       reach the machine over TCP, overriding what the script says
              --serial <port>         reach it over a serial port, such as COM12
              --loopback              run against the software machine in this process
              --address <hh>          poll address, in hex, overriding the script
              --channel <1-4>         which channel to drive
              --poll <duration>       poll interval, such as 200ms
              --stimulus <backend>    none, manual, software or qpm  (default: none)
              --real-time-events      let cases ask the machine to report exceptions in the
                                      real-time event format. Off by default: turning it on
                                      changes how the machine answers every general poll
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
              sasctl catalogue events            what an exception carries after its code
              sasctl catalogue diff [options]    compare this build's catalogue against two
                                                 descriptions of SAS written by other people

              --legacy <file>         the legacy tester's configuration file
              --toolkit <directory>   the conformance toolkit's directory, which must hold its
                                      poll, response and event templates and their definition
              --all                   print every code, not only the ones something disagrees on

              The diff is read-only in both directions: their files are not changed and nothing
              from them is written here. It reports what to model next, not what to copy.

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

            EXPLAIN
              --out <file>            write the log to a file instead of the screen

              Reads a run written with --report jsonl and renders the same extended log that
              'run --report extended' produces during a run. It is a rendering of the record,
              never a second recording, so an explanation that turns out wrong is corrected and
              rendered again without re-running anything.

              Every packet gets a line. A reply whose layout this build has not established is
              named and marked 'known but not decoded' rather than guessed at, and the tally at
              the end says how many packets were explained in full and how many only named — so
              the log measures how much of the protocol this build understands.

            IMPORT
              --out <dir>             where to write the record  (default: runs/<time>-import)
              --limit <n>             stop after this many messages, and say so in the record
              --report <list>         which reports to write  (default: jsonl,extended)

              Reads the conformance toolkit's traces and its session log, and the legacy tester's
              serial log, and writes the same record a run writes — so 'explain' reads an import
              with no knowledge that captures exist.

              Why it is worth doing: our own suite exchanges only what it was taught to exchange,
              so a coverage figure taken over it measures the script rather than the protocol. A
              recording of another tool driving a real machine does not flatter anything.

              An import asserts nothing and comes back as a skipped run, because reading somebody
              else's traffic proves nothing about this build and must not be able to look like a
              pass. Where a recording kept only eight data bits, the record says the wakeup bit
              was never captured rather than putting it where the protocol would want it.

            SWEEP
              --tcp <host:port>       reach the machine over TCP, listening unless --dial
              --serial <port>         reach it over a serial port
              --address <hh>          poll address, in hex  (default: 01)
              --list                  print what would be asked and what would not, and stop
              --framing               measure the check-sequence rule on the wire instead of
                                      sweeping. This one sends $01 and $02, which the sweep
                                      itself withholds, and sends $02 last so the machine is
                                      left playing.
              --out <directory>       where to write the record  (default: runs/<time>-sweep)
              --report <formats>      which reports to write  (default: jsonl,extended)

              Asks the machine every command in the catalogue that can be asked, and writes down
              what came back. A coverage figure taken over traffic can only name the commands
              somebody happened to send; this produces bytes for the rest, which is also the only
              thing that verifies a layout rather than asserting it.

              A command that changes the machine is not sent. Every one is listed by name with
              what it would have changed - '--list' prints the whole classification without
              opening a link. A command that answers nothing is recorded as answering nothing,
              because a machine's silence is a fact about the machine.

              It asserts nothing, so like an import it comes back as a run in which nothing was
              tested rather than as a pass.

            EVENTS
              --tcp <host:port>       reach the machine over TCP, listening unless --dial
              --serial <port>         reach it over a serial port
              --address <hh>          poll address, in hex  (default: 01)
              --list                  print what would be caused and what cannot be, and stop
              --seconds <n>           how long to watch after the machine is set going
                                      (default: 45)
              --note <cents>          what note to offer the acceptor  (default: 2000)
              --egm-resources <dir>   the machine's own resource directory, which is where it
                                      declares its doors and buttons
              --qpm-shm <file>        the peripheral shared region, if it is not in the usual
                                      place for this platform
              --out <directory>       where to write the record  (default: runs/<time>-events)
              --report <formats>      which reports to write  (default: jsonl,extended)

              Turns real-time event reporting on, makes the machine do the things that report an
              event, and writes down what each one carried. A long poll can be asked for and an
              event can only be caused, which is why this is a command of its own.

              It sends two commands the sweep withholds and says so before opening the link: $0E,
              because turning reporting on is the measurement, and $AA, because the machine
              playing by itself is the only lever here that produces a game. Both are turned off
              again at the end and the run says whether that was acknowledged.

              An exception nothing here can cause is reported as unproduced with the reason,
              never as a payload nobody checked. It asserts nothing, so it comes back as a run in
              which nothing was tested rather than as a pass.

            NVRAM
              status                  what the machine holds, and what has been saved
              show                    what each is configured to be, side by side
              save                    save the machine's memory as a fixture
              restore                 write a saved fixture back over it, keeping what was there
              validation <style>      write a fixture out again in standard, secure_enhanced or
                                      system validation

              --fixture <file>        which saved memory  (default: fixtures/nvram/golden.nvram.gz)
              --nvram <file>          the machine's memory, if it is not in the usual place
              --out <file>            where 'validation' writes  (default: over the fixture)

              A machine's note acceptor, its printer, which notes it takes and which validation
              style it performs live in this memory and are otherwise set through its own menu, by
              hand. Save and restore refuse while a machine is running, because a copy taken or
              written under one is a configuration nobody had.

              A memory records the arrangement it was written in, and a machine clears any memory
              not written in the arrangement it was built for — so a fixture and a build that
              disagree cannot be used together, and every action here says so rather than reading
              a configuration out of the wrong bytes.

            EXAMPLES
              sasctl run tests/regression --loopback --stimulus software
              sasctl run tests/conformance/10.1 --tcp 127.0.0.1:7200 --report html,junit
              sasctl probe --serial COM12
              sasctl probe --stimulus qpm --egm-resources <demo>/Resources
              sasctl egm --tcp 127.0.0.1:7200
              sasctl qpm status --egm-resources <demo>/Resources
              sasctl qpm audit open --egm-resources <demo>/Resources
              sasctl explain runs/20260907-101500/run.jsonl
              sasctl sweep --list
              sasctl sweep --tcp 127.0.0.1:9760
              sasctl nvram show
              sasctl nvram validation secure_enhanced --out fixtures/nvram/secure-enhanced.nvram.gz
              sasctl nvram restore --fixture fixtures/nvram/secure-enhanced.nvram.gz
            """);
    }
}
