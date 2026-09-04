#!/usr/bin/env python3
"""M0 wire probe - a throwaway SAS host that only proves the link exists.

Listens for the qxcom-tcp transport, sends a general poll and long poll 1F
(send gaming machine ID and information), and dumps every symbol in both
directions. It implements no SAS state machine and answers nothing: its whole
job is to show that a real libsas, running inside QSP_DemoOpenGL with no
Quixant board, is reachable over the wire.

Wire format: one symbol per little-endian uint16, bit 8 = wakeup.
"""

import argparse
import socket
import struct
import sys
import threading
import time

WAKEUP = 0x100

_start = time.monotonic()
_print_lock = threading.Lock()


def stamp():
    return f"{(time.monotonic() - _start) * 1000:9.2f}ms"


def render(symbols):
    """'1.81 0.1F' - wakeup flag and payload, one group per symbol."""
    return " ".join(f"{'1' if s & WAKEUP else '0'}.{s & 0xFF:02X}" for s in symbols)


def log(direction, symbols, note=""):
    with _print_lock:
        suffix = f"   {note}" if note else ""
        print(f"[{stamp()}] {direction} {render(symbols)}{suffix}", flush=True)


def encode(symbols):
    return b"".join(struct.pack("<H", s & 0x1FF) for s in symbols)


class Reader(threading.Thread):
    """Drains the socket, reassembles symbols, and prints one line per message.

    Messages are separated by a gap in arrival time: SAS gives no framing, so a
    quiet period is the only thing that says one response ended and the next
    began. GAP_S is far longer than the inter-byte time of a single response and
    far shorter than the poll interval, so the split is unambiguous here.
    """

    GAP_S = 0.02

    def __init__(self, conn):
        super().__init__(daemon=True)
        self.conn = conn
        self.running = True
        self.received = []
        self.messages = []
        self._group = []
        self._last = 0.0

    def _flush(self):
        if self._group:
            self.messages.append(list(self._group))
            log("RX", self._group)
            self._group = []

    def run(self):
        pending = b""
        self.conn.settimeout(self.GAP_S)
        while self.running:
            try:
                chunk = self.conn.recv(4096)
            except socket.timeout:
                self._flush()
                continue
            except OSError:
                self._flush()
                return
            if not chunk:
                self._flush()
                with _print_lock:
                    print(f"[{stamp()}] --  peer closed the connection", flush=True)
                return
            now = time.monotonic()
            if self._group and now - self._last > self.GAP_S:
                self._flush()
            self._last = now
            pending += chunk
            while len(pending) >= 2:
                (symbol,), pending = struct.unpack("<H", pending[:2]), pending[2:]
                self.received.append(symbol)
                self._group.append(symbol)


def send(conn, symbols, note=""):
    log("TX", symbols, note)
    conn.sendall(encode(symbols))


def general_poll(address):
    #   A general poll is the address with bit 7 set, and it is the only byte of the
    #   message, so the wakeup bit rides on it alone.
    return [WAKEUP | 0x80 | address]


def sync_poll():
    #   A general poll of address zero. After power-up or a loop break the machine
    #   ignores every poll addressed to itself until it has seen either this or a poll
    #   for a different machine, so a host that only ever polls one machine at its own
    #   address is answered by silence. SAS 6.02 section 3.3.
    return [WAKEUP | 0x80]


def long_poll(address, command):
    #   Address byte carries the wakeup bit; every following byte must not.
    return [WAKEUP | address, command]


def run_session(conn, args):
    reader = Reader(conn)
    reader.start()

    if args.sweep:
        print(f"[{stamp()}] --  sweeping addresses 1..127 with long poll 1F", flush=True)
        for address in range(1, 128):
            before = len(reader.received)
            send(conn, sync_poll(), "sync poll")
            time.sleep(args.interval)
            send(conn, long_poll(address, 0x1F), f"address {address}")
            time.sleep(args.interval)
            if len(reader.received) > before:
                print(f"[{stamp()}] --  address {address} answered", flush=True)
        return

    for _ in range(args.polls):
        send(conn, sync_poll(), "sync poll - address zero")
        time.sleep(args.interval)
        send(conn, general_poll(args.address), "general poll")
        time.sleep(args.interval)

    send(conn, long_poll(args.address, 0x1F), "long poll 1F - send gaming machine ID")
    time.sleep(args.settle)

    reader.running = False
    reader.join(timeout=1.0)
    print(f"[{stamp()}] --  {len(reader.received)} symbol(s) in {len(reader.messages)} "
          f"message(s) received from the EGM", flush=True)

    wakeup_on_non_address = [m for m in reader.messages if any(s & WAKEUP for s in m)]
    print(f"[{stamp()}] --  EGM messages carrying a wakeup bit: "
          f"{len(wakeup_on_non_address)} (expected 0)", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9760)
    parser.add_argument("--address", type=int, default=1, help="SAS poll address of the EGM")
    parser.add_argument("--polls", type=int, default=10, help="general polls before the long poll")
    parser.add_argument("--interval", type=float, default=0.2, help="seconds between polls")
    parser.add_argument("--settle", type=float, default=2.0,
                        help="seconds to keep listening after the last poll")
    parser.add_argument("--sweep", action="store_true",
                        help="probe every address 1..127 to discover the configured one")
    parser.add_argument("--wait", type=float, default=120.0,
                        help="seconds to wait for the EGM to connect")
    args = parser.parse_args()

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind((args.host, args.port))
    listener.listen(1)
    listener.settimeout(args.wait)
    print(f"[{stamp()}] --  listening on {args.host}:{args.port}, waiting for the EGM", flush=True)

    try:
        conn, peer = listener.accept()
    except socket.timeout:
        print(f"[{stamp()}] --  no EGM connected within {args.wait:.0f}s", flush=True)
        return 1

    print(f"[{stamp()}] --  EGM connected from {peer[0]}:{peer[1]}", flush=True)
    conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    try:
        run_session(conn, args)
    finally:
        conn.close()
        listener.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
