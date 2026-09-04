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

            RUN
              --tcp <host:port>       reach the machine over TCP, overriding what the script says
              --serial <port>         reach it over a serial port, such as COM12
              --loopback              run against the software machine in this process
              --address <hh>          poll address, in hex, overriding the script
              --channel <1-4>         which channel to drive
              --poll <duration>       poll interval, such as 200ms
              --stimulus <backend>    none, manual, software or qpm  (default: none)
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

            EXAMPLES
              sasctl run tests/regression --loopback --stimulus software
              sasctl run tests/conformance/10.1 --tcp 127.0.0.1:7200 --report html,junit
              sasctl probe --serial COM12
              sasctl egm --tcp 127.0.0.1:7200
            """);
    }
}
