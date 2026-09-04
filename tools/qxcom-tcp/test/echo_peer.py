#!/usr/bin/env python3
"""Echo peer for the qxcom-tcp round-trip check: accepts one connection and
sends every byte straight back."""

import socket
import sys

host, _, port = (sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1:9761").rpartition(":")
listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
listener.bind((host or "127.0.0.1", int(port)))
listener.listen(1)
listener.settimeout(20)
print(f"echo peer listening on {host or '127.0.0.1'}:{port}", flush=True)

conn, peer = listener.accept()
print(f"peer connected from {peer[0]}:{peer[1]}", flush=True)
conn.settimeout(5)
try:
    while True:
        chunk = conn.recv(4096)
        if not chunk:
            break
        conn.sendall(chunk)
except (socket.timeout, OSError):
    pass
finally:
    conn.close()
    listener.close()
print("echo peer done", flush=True)
