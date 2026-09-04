# qxcom-tcp

The missing `libqxcom` implementation, over TCP.

`QSP/CMakeLists.txt` has carried `option(SIMULATE_QXCOM ...)` and
`QSP/simulator/include/qxcom/` for a long time, but only the headers were ever
written. This directory supplies the implementation behind that option: it
satisfies the existing header contract exactly, and moves 9-bit SAS symbols over
a TCP socket instead of `\.\QXCOM`.

Nothing here is added to the QSP or QSP_DemoOpenGL repositories.

## Wire format

One SAS symbol per little-endian `uint16`:

```
bit 15..9   zero
bit 8       wakeup / address bit
bit 7..0    data
```

This is the whole point of the transport (see `.planning/DECISIONS.md` D2): a
virtual COM pair carries 8-bit bytes and would silently lose the wakeup bit,
which is the single most protocol-critical signal in SAS.

## Configuration

Read from the environment when a port is opened:

| Variable | Default | Meaning |
|---|---|---|
| `SASWB_QXCOM_ENDPOINT` | `127.0.0.1:9760` | `host:port` of the peer |
| `SASWB_QXCOM_ENDPOINT_<PORT>` | — | per-port override; `<PORT>` is the port name upper-cased with non-alphanumerics replaced by `_` (so `/dev/ttyS1` and `COM3` become `_DEV_TTYS1` and `COM3`) |
| `SASWB_QXCOM_MODE` | `connect` | `connect` (dial the peer) or `listen` (accept one peer) |
| `SASWB_QXCOM_POLL_MS` | `2` | how long a read with no data waits before returning empty |
| `SASWB_QXCOM_LOG` | — | file to append a symbol-level trace to; `-` means stderr |

An unconnected link behaves like an open serial port with nothing attached:
opening succeeds, reads return no data, and a background thread keeps trying to
reconnect. That matches real serial semantics and lets the EGM and the host
start in either order.

## What it refuses to do

The 8-bit `read`/`write` callbacks return an error instead of transferring data.
An 8-bit path cannot represent the wakeup bit, and silently dropping it would
leave a broken link looking healthy. Only the `readExtended`/`writeExtended`
9-bit path is implemented — which is the path libsas uses.
