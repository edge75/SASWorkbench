# M8 — Serial SAS on Linux

`sasctl` and the window now drive a real serial port on Linux (x86-64 and ARM64), with the wakeup
bit. Until now a serial link on Linux was refused, because .NET's serial layer drops mark and
space parity there without an error (D20; BLOCKERS §6).

## How the ninth bit is carried

The port is driven with the kernel's own terminal requests, not through `System.IO.Ports` or a C
library's termios wrappers. The wakeup bit is **stick parity**: `PARENB | CMSPAR`, with `PARODD`
set for the address byte (mark, ninth bit 1) and cleared for every other byte (space, ninth bit 0).

| Setting | Value | Why |
|---|---|---|
| Frame | 19200 baud, 8 data bits, stick parity, 1 stop bit | SAS framing |
| Flow control | none, `CLOCAL`, `CREAD` | a gaming machine has none |
| Input | raw, **no parity check** (`INPCK`, `IGNPAR`, `PARMRK`, `ISTRIP` off) | in mark, every byte a machine sends is a parity error; it must arrive as it was sent |
| Reads | `VMIN = 0`, `VTIME = 0` | the reader keeps its own deadline |
| Parity switch | drain (`tcdrain`), one more character time, then set | the byte still in the transmitter is never re-framed |

The parity is switched only where it changes, as on Windows: a one-byte general poll costs no
switch, a long poll costs two.

## Refusal, never degradation

On open, the port is set to mark and read back, then set to space and read back. If the driver
changed any framing bit — `CMSPAR`, `PARENB`, `PARODD`, the size, the stop bits or the speed — the
open is **refused** with the driver's name and the bit it changed. A driver that forgets `CMSPAR`
would turn mark/space into odd/even parity, and the address byte would carry a wakeup bit only when
its data happened to need one; that is the silent failure this refuses.

A pseudo-terminal clears `PARENB` on every request, so the unit tests use one to prove the refusal
end to end through the real kernel.

What a read-back cannot prove: that the hardware puts the bit the driver accepted on the wire. A
link on Linux is therefore labelled **unverified**, like every serial link, until it is measured.

## `sasctl probe --serial <port>`

```
sasctl probe --serial /dev/ttyS4
  /dev/ttyS4  carries the wakeup bit — /dev/ttyS4 (driver serial8250) kept mark and space stick parity at 19200 baud.
```

It opens one port, does the same mark/space read-back, and puts the port's settings back. It exits
1 when the port cannot carry the wakeup bit. A short name (`ttyS4`) is looked up under `/dev`. Do
not probe a port another program is using: its settings change for a moment.

The driver name comes from sysfs and tells an on-board UART (`serial8250`) from a USB adapter
(`ftdi_sio`, `pl2303`, `cp210x`, …). **USB adapters are unmeasured.**

## What is supported

| Platform | Serial link |
|---|---|
| Windows | unchanged: `Parity.Mark`/`Parity.Space`, measured on a Quixant IQ-2 on-board UART |
| Linux x86-64 / ARM64, on-board UART | supported; read-back checked at open; **not yet measured on a board** |
| Linux, USB serial adapter | opens if its driver keeps stick parity; timing unmeasured |
| Anywhere else | refused |

QXCOM true 9-bit (`IOCTL_COM_EXTEND_DATABITS`) is not used. It stays the next rung if a board's
driver refuses stick parity.

## Step 3 — the loopback on the IQON-3 (needs a person to move a cable)

Not done yet. It measures the product on the board against two existing numbers:

| Reference | 1C replies | median | max |
|---|---|---|---|
| 20-11, IQON-3, local 9-bit bridge over TCP (not published) | 2000 | 49.9 ms | 57 ms |
| 23-11, IQ-2 COM6 → IQON-3 ttyS5, no door, ~200 ms cadence | 1200 | 60.8 ms | 87.7 ms (p99 78.3) |

1. **Cable.** Move the demo printer off `ttyS4`. Move the SAS cable from the IQ-2's COM6 to the
   IQON-3's `ttyS4`, so `ttyS4` ↔ `ttyS5` is a null-modem loopback. The demo keeps `Network 1` =
   `/dev/ttyS5`.
2. **Build** on Windows, from this repository:
   `dotnet publish src/SasWorkbench.Cli -c Release -r linux-x64 --self-contained -o out/linux-x64`,
   then copy `out/linux-x64` to the board (`~/sasctl-linux`). Do not rebuild or use the local
   9-bit bridge from 20-11.
3. **Permissions.** `ls -l /dev/ttyS4`; the user must be in `dialout`, or run with `sudo -n`.
4. **Probe**, demo running: `~/sasctl-linux/sasctl probe --serial /dev/ttyS4`. Expect
   `carries the wakeup bit` and `driver serial8250`. Never probe `ttyS5` while the demo has it.
5. **Link up and general poll:**
   `~/sasctl-linux/sasctl run tests/regression/wire/link-comes-up.sas --serial /dev/ttyS4 --report jsonl,html`.
6. **1C in a loop and door exceptions:** run the same `door-1c.sas` the 23-11 IQ-2 run used
   (address 01, general poll every 200 ms, one 1C about every 200 ms, about 7 minutes) with
   `--serial /dev/ttyS4 --report jsonl,html`, once with no door activity and once opening and
   closing the main door four times. Expect $11 on each open and $12 on each close.
7. **Compare** the 1C reply latency (median, p99, max, any reply over 100 ms or missing) with the
   table above. Also run `sasctl floor --serial /dev/ttyS4 --addresses 01 --rounds 2000` for the
   bare poll round.
8. **Put back** the printer on `ttyS4` and the SAS cable to the IQ-2's COM6.

A pass on step 5 proves the wakeup bit reaches the demo. A median near 50–61 ms says the Linux
backend adds nothing that matters; a door-close delay is the known platform issue, not the link.
