# Runbook — running this suite on a laptop and against a real machine

Everything here has been run. Where something has not, it says so.

---

## 0. What you need once

| | |
|---|---|
| .NET | 10 SDK. `dotnet --version` |
| Python | 3.11+, with `pdfplumber`, only for re-reading the test-script PDFs |
| Repository | `C:\Progetti\SasWorkbench` |
| Related checkouts | `C:\Progetti\libsas`, `C:\Progetti\QSP`, `C:\Progetti\QSP_DemoOpenGL`, `C:\Progetti\qpm_simulator` — read-only except where this file says otherwise |

Build and check the whole thing:

```
dotnet build SasWorkbench.slnx
dotnet test  SasWorkbench.slnx
```

984 unit tests across four assemblies. They need no machine and no hardware.

---

## 1. On a laptop, against the software machine

This is the everyday loop. Nothing outside this repository is involved: the host and the machine
are both in this process, over an in-memory link.

```
sasctl probe  --loopback --stimulus software
sasctl run tests/regression  --loopback --stimulus software
sasctl run tests/conformance --loopback --stimulus software --skip-tag finding
sasctl run tests/validation/egm-test-scripts-v1.00 --loopback --stimulus software
```

`sasctl` is `src/SasWorkbench.Cli/bin/Debug/net10.0/sasctl.exe`, or `dotnet run --project
src/SasWorkbench.Cli --`.

What each tree is for:

| tree | what it proves |
|---|---|
| `tests/regression/` | this build's own behaviour — framing, channels, meters, tickets, the poll engine |
| `tests/conformance/` | the GSA toolkit corpus, revision 1.2 |
| `tests/validation/egm-test-scripts-v1.00/` | the eleven EGM test-script PDFs, revision 1.00 |

**A loopback pass proves our framing, not a gaming machine's.** Every report says so in its own
provenance line. Do not quote a loopback run as evidence about a machine.

### The two conformance gates

One case fails **on purpose** — it is a finding about the machine library, not a defect here. Two
commands keep that honest, and CI runs both:

```
sasctl run tests/conformance --loopback --stimulus software --skip-tag finding   # must exit 0
sasctl run tests/conformance --loopback --stimulus software --tag finding        # must exit 1
```

A bare `sasctl run tests/conformance` goes red and says `1 failed (1 labelled a finding)`. That is
expected.

### Exit codes

| | |
|---|---|
| 0 | every test that ran passed |
| 1 | at least one test failed |
| 2 | the tool could not do what was asked |
| 3 | **nothing was tested** — every test was skipped. Deliberately not a success. |

---

## 2. On a laptop, against the demo machine over TCP

The demo EGM runs in another process and the SAS link is carried over a TCP socket.

1. Check the pinned libsas revision, because EGM behaviour changes underneath a passing suite if
   it moves:

   ```
   git -C C:/Progetti/libsas rev-parse HEAD
   ```

   It must equal the commit in `fixtures/pinned-versions.json`
   (`4666881b3272d94969babc86076fb90664deae53`), and the checkout must be clean.

2. Build the demo with the preset this repository owns. It is the **only** file added to
   `QSP_DemoOpenGL`, and it is gitignored there (D30, CLAUDE.md constraint 3):

   ```
   cd C:\Progetti\QSP_DemoOpenGL
   cmake --preset SasWorkbenchM0
   cmake --build out/build/SasWorkbenchM0
   ```

   It must be built **Release**. A Debug build of the platform needs the `DEMO` licence feature on
   top of `SAS`, and the key in this machine's secure storage carries `SAS` only — built Debug the
   machine raises a licence lockup and takes no money (BLOCKERS.md Trap D).

3. Restore the memory the machine needs. Fresh NVRAM has no printer protocol and no validation
   style, so no ticket prints and no note is accepted:

   ```
   sasctl nvram restore --fixture fixtures/nvram/golden.nvram.gz
   ```

   Use `secure-enhanced.nvram.gz` instead for the redemption cases. The fixtures are **arrangement
   7**, which is the QSP the preset fetches. The read-only checkout at `C:\Progetti\QSP` is
   arrangement 6 and must not be used as the build source — a machine master-resets memory not
   written in the arrangement it was built for.

4. Start the machine, then point the host at it:

   ```
   tools\qxcom-tcp\run-m0.ps1
   sasctl run tests/regression --tcp 127.0.0.1:9760 --address 01 --stimulus qpm
   ```

---

## 3. Against a real machine, over a real serial port

**Not yet measured.** The serial transport is implemented and the ports are detected, but no run
in this project has gone over an adapter. Treat a first pass as untested until the wire has been
watched (D6). Everything below is the procedure; the caveat is part of it.

### The wire

SAS is a 9-bit protocol and the ninth bit is the wakeup bit, which marks an address byte. The
transport carries it as a parity change — `Mark` for a wakeup symbol, `Space` for a data symbol —
so the adapter must honour per-byte parity switching. An adapter that buffers or coalesces writes
breaks this, and the failure looks like a machine that never answers.

| | |
|---|---|
| baud | 19200 |
| data bits | 8 |
| parity | Space for data, Mark for an address — switched per symbol |
| stop bits | 1 |

### Steps

1. Find the port:

   ```
   sasctl probe
   ```

   The `serial` line lists what Windows reports. On this laptop today: COM3, COM4, COM5, COM6.

2. Confirm the machine's poll address, and set it if it is not 1. The address lives in the
   machine's memory and is reached from its own audit menu, not over the link.

3. Bring the link up on its own before running anything that asserts:

   ```
   sasctl run tests/regression/wire --serial COM3 --address 01
   ```

   If the link does not come up, check in this order: the port exists, the adapter switches parity
   per byte, the address matches, and the machine is powered and past its boot.

4. Then the suites. Stimulus is `manual` on a machine nobody can drive from software — a step that
   needs a person prompts and waits, and skips with a reason if nobody answers:

   ```
   sasctl run tests/regression --serial COM3 --address 01 --stimulus manual
   sasctl run tests/validation/egm-test-scripts-v1.00 --serial COM3 --address 01 --stimulus manual --ask-for 5m
   ```

5. Reports, for anything worth keeping:

   ```
   sasctl run <tree> --serial COM3 --address 01 --report html,junit,jsonl --out runs/<name>
   ```

### Two things to know before trusting a serial run

* **Real-time event reporting is off unless asked for.** `--real-time-events` turns it on, and
  turning it on changes how the machine answers *every* general poll afterwards. A run that enables
  it leaves the machine changed for whatever runs next.
* **A long poll sent while an exception is queued is answered with the event, not the reply.** The
  poll reports `AnsweredWithEvent`, which is not a success, and the step fails telling you to drain
  the queue first. That is the machine behaving correctly, not a fault.

---

## 4. Reading a result

* A **skip** always carries the name of what was missing. `sasctl probe` lists what the environment
  provides, and a skip reason quotes the same names.
* A run where everything skipped exits **3**, not 0.
* Every report carries a provenance line naming the link, its fidelity, the stimulus backend and
  the channel map. A loopback line says in words that it proves our framing only.
* `sasctl log` packs a run into one container; `sasctl explain` reads a recording back packet by
  packet.

---

## 5. What is not here

| | |
|---|---|
| a hand pay | nothing in this build can force one; the software machine has no hand pay at all and answers `$1B` with zeros. 168 corpus cases wait on it. |
| comparing the wire against the machine's own display | always a person. 142 cases. |
| moving funds, progressives, a hopper | no stimulus |
| Linux | the serial backend and the rig are Windows-only today (D20) |
