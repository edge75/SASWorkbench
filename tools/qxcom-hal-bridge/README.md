# qxcom-hal-bridge

Puts SasWorkbench on the SAS link of an unmodified HAL build of the demo (`HALRelease`).

A HAL build speaks SAS on a libqxcom virtual serial port: a cable in shared memory with two ends.
This program opens the other end and relays every unit, wakeup bit included, to SasWorkbench over
the same TCP format qxcom-tcp uses. No special build of the demo is needed.

## Build

```
cmake -S tools/qxcom-hal-bridge -B tools/qxcom-hal-bridge/build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build tools/qxcom-hal-bridge/build
```

## Run

1. Start `qpm_simulator`, then the HALRelease `OpenGL_Game.exe`. Keep the simulator window
   visible: it files the tickets the machine prints.
2. Start the host, which listens: `sasctl run tools/qxcom-hal-bridge/hal-tickets.sas --tcp 127.0.0.1:9760`
   (or the SasWorkbench window, transport tcp, 127.0.0.1:9760).
3. Start the bridge, which dials it: `qxcom-hal-bridge /dev/ttyS5 127.0.0.1:9760`.
   The port is the machine's `Serial Ports` › `Network 1`.

`hal-tickets.sas` needs a person: insert the ticket with validation number 1234567890123456 for
$20.00 when the first case waits, then press Collect on the game when the second case waits.
The machine must be on secure enhanced validation with ticket in enabled, and have credit.

`hal-unattended.sas` needs nobody. Close the `qpm_simulator` window first, because it rewrites
the peripheral region on every frame, then add the stimulus:
`sasctl run tools/qxcom-hal-bridge/hal-unattended.sas --tcp 127.0.0.1:9760 --stimulus qpm --egm-resources <machine>/Resources`.
It inserts notes and redeems a ticket. Printing still needs the simulator, which is the printer:
without it a cash-out raises Printer Malfunction, and the reset key clears it.

## If a cash-out that should print pays by handpay

Earlier runs can leave records a host never read: exception 51 (handpay information), 68
(ticket status) and 3D (the validation buffer). A validation record counts as read only once the
host polls again after long poll 4D, so a script that ends on 4D leaves it unread, and ten seconds
later the machine stops using the printer as the cash-out device. Run
`sasctl run tools/qxcom-hal-bridge/hal-read-back.sas --tcp 127.0.0.1:9760`, then cash out again.
If it still fails on exception 51, more handpay records were queued than one run reads: run it
again.

## If the port will not open

On Windows a virtual cable keeps the slot of a process that was killed rather than closed. When
nothing that uses the cable is running, delete `C:\ProgramData\shared_memory\lsm_qxcom_5.shm`.
