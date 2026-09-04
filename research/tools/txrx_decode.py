#!/usr/bin/env python3
r"""
Decoder for sastest.exe's TXRXData.dat serial capture log.

FORMAT (reverse-engineered from C:\Progetti\libsas\utils\igt\TXRXData.dat, 3967 B,
and verified over the whole of C:\IGT3\TXRXData.dat, 39,357,255 B / 2,248,986 lines):

  Plain ASCII, CRLF line terminators. Exactly three line kinds, no header, no footer:

    <ctime>        e.g. "Thu Mar 02 04:11:52 2017"   C asctime()/ctime() form, 1-second
                   resolution, no timezone, no milliseconds.
    TX>= <hex>     bytes the tester sent,     uppercase hex, space separated
    RX<= <hex>     bytes the tester received, uppercase hex, space separated

  A record is:  one timestamp line, then exactly one TX line, then ZERO OR ONE RX line.
  Verified: 39MB file has 1,124,493 TX lines and 1,124,493 timestamp lines -> 1:1.
  A record with no RX line is a NO-RESPONSE / timeout.
  There is NO direction flag byte, NO length field, NO sequence number and NO
  explicit duration: everything is inferred from line prefix and ordering.
  Endianness is not applicable (text); SAS payload CRC is little-endian (LSB first).

Usage:  python txrx_decode.py <file> [max_records]
"""
import sys, re, datetime

TS_RE = re.compile(r'^[A-Z][a-z]{2} [A-Z][a-z]{2} [ 0-9][0-9] \d{2}:\d{2}:\d{2} \d{4}$')

# SAS CRC-16, transcribed from crcCalc() in
# C:\Progetti\libsas\src\libsas_utils.cpp lines 97-113 (poly 0x1081 nibble table form).
def sas_crc(data):
    crc = 0
    for c in data:
        q = (crc ^ c) & 0o17
        crc = (crc >> 4) ^ (q * 0o10201)
        q = (crc ^ (c >> 4)) & 0o17
        crc = (crc >> 4) ^ (q * 0o10201)
    return crc & 0xFFFF            # wire order = low byte first

# General/sync polls seen in the captures. Names taken verbatim from the [Script]
# section quoted in SASTEST.HLP: "81 = Sending General Poll", "80 = Sending Re-Sync".
POLL_NAMES = {0x80: "Re-Sync / general poll (even)", 0x81: "General Poll (odd)"}

# Long-poll names quoted from the [Messages]/[Script] sections of SASTEST.HLP.
LP_NAMES = {
    0x0F: "Send selected meters (multi)", 0x10: "Send canceled credits meter",
    0x11: "Send coin in meter",           0x12: "Send coin out meter",
    0x13: "Send drop meter",              0x14: "Send jackpot meter",
    0x15: "Send games played meter",      0x19: "Send meters $11-$15",
    0x1A: "Send current credits",         0x1B: "Send handpay information",
    0x1F: "Send gaming machine ID",       0x20: "Send dollar value of bill meter",
    0x2A: "Send true coin in",            0x2B: "Send true coin out",
    0x2F: "Send selected meters for game N",
    0x31: "Send $1 bill meter",  0x35: "Send $20 bill meter",
    0x36: "Send $50 bill meter", 0x37: "Send $100 bill meter",
}

def parse(path, limit=None):
    ts = None
    tx = None
    recs = []
    with open(path, 'r', encoding='latin-1', errors='replace') as f:
        for raw in f:
            line = raw.rstrip('\r\n')
            if not line:
                continue
            if line.startswith('TX>= '):
                if tx is not None:                 # previous TX never got an RX
                    recs.append((ts, tx, None))
                    if limit and len(recs) >= limit: return recs
                tx = bytes.fromhex(line[5:].replace(' ', ''))
            elif line.startswith('RX<= '):
                rx = bytes.fromhex(line[5:].replace(' ', ''))
                recs.append((ts, tx, rx))
                tx = None
                if limit and len(recs) >= limit: return recs
            elif TS_RE.match(line):
                ts = line
            else:
                sys.stderr.write("UNPARSED: %r\n" % line)
    if tx is not None:
        recs.append((ts, tx, None))
    return recs

def describe(b):
    """Human-readable gloss for one SAS frame."""
    if not b: return ""
    if len(b) == 1:
        return POLL_NAMES.get(b[0], "single byte (addr/poll 0x%02X)" % b[0])
    addr, cmd = b[0], b[1]
    name = LP_NAMES.get(cmd, "long poll $%02X" % cmd)
    out = "addr=%d cmd=$%02X %s" % (addr, cmd, name)
    if len(b) >= 4:                       # frames >=4 bytes carry a trailing CRC
        want = sas_crc(b[:-2])
        got  = b[-2] | (b[-1] << 8)       # LSB first on the wire
        out += "  CRC=%s(calc %04X, wire %04X)" % ("OK " if want == got else "BAD", want, got)
    return out

def main():
    path = sys.argv[1] if len(sys.argv) > 1 else r"C:\Progetti\libsas\utils\igt\TXRXData.dat"
    limit = int(sys.argv[2]) if len(sys.argv) > 2 else 50
    recs = parse(path, limit)
    print("FILE: %s" % path)
    print("%-4s %-24s %-6s %s" % ("#", "TIMESTAMP", "DIR", "BYTES / MEANING"))
    print("-" * 108)
    for i, (ts, tx, rx) in enumerate(recs):
        if tx is None:   # orphan RX: file begins part-way through a transaction
            print("%-4d %-24s TX>=  %-34s *** MISSING (log starts mid-transaction) ***"
                  % (i, ts or "(none)", "--"))
        else:
            print("%-4d %-24s TX>=  %-34s %s" % (i, ts or "(none)", tx.hex(' ').upper(), describe(tx)))
        if rx is None:
            print("%-4s %-24s RX<=  %-34s *** NO RESPONSE (timeout) ***" % ("", "", "--"))
        else:
            print("%-4s %-24s RX<=  %-34s %s" % ("", "", rx.hex(' ').upper(), describe(rx)))
    return recs

if __name__ == "__main__":
    main()
