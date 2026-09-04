"""SASHostLogNNNNN.log  --  binary event log written by SASHostSim.exe.

Layout (little-endian):

    file := record*
    record := WORD  version      (always 0x0001 in v1.1)
              DWORD unixTime     (time_t, seconds, local-clock based)
              WORD  payloadLen
              BYTE  payload[payloadLen]

    payload := BYTE recordType, then type-specific body

      type 0x00  text  : ASCII text, payloadLen-1 bytes, no NUL.
                         Used for  "*** Log started at MM/DD/YY HH:MM:SS ***",
                         "*** Log stopped at ... ***" and "USER ACTION -- ...".
      type 0x44  data  : one SAS message (one direction)
                         WORD  messageIndex   (monotonic per log file, wraps at 0x10000)
                         DWORD startTicks     (GetTickCount-style ms at first byte)
                         WORD  byteCount
                         BYTE  direction      (0 = EGM->host, 1 = host->EGM)
                         then byteCount x { BYTE value ; WORD ticksLow16 }
                         ticksLow16 is the low 16 bits of the same ms counter,
                         i.e. the per-byte arrival/emission time. Subtracting
                         consecutive values gives the inter-byte gap in ms.

Note the wakeup (9th) bit is NOT stored: only the 8 data bits survive.
"""
import sys, os, struct, datetime
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import R, load

TYPE_TEXT = 0x00
TYPE_DATA = 0x44
DIRN = {0: 'EGM->HOST', 1: 'HOST->EGM'}


def decode(path, limit=None):
    d = load(path)
    r = R(d)
    n = 0
    while not r.eof():
        ver = r.u16()
        t = r.u32()
        ln = r.u16()
        body = r.blob(ln)
        ts = datetime.datetime.utcfromtimestamp(t).strftime('%Y-%m-%d %H:%M:%SZ')
        if not body:
            print('%s  ver=%d  EMPTY' % (ts, ver)); continue
        rt = body[0]
        if rt == TYPE_TEXT:
            print('%s  TEXT  %s' % (ts, body[1:].decode('latin-1')))
        elif rt == TYPE_DATA:
            idx, tick, cnt = struct.unpack_from('<HIH', body, 1)
            dirn = body[9]
            bs = []
            o = 10
            for _ in range(cnt):
                v, tl = struct.unpack_from('<BH', body, o); o += 3
                bs.append((v, tl))
            base = bs[0][1] if bs else 0
            hexs = ' '.join('%02X' % v for v, _ in bs)
            gaps = ' '.join('+%d' % ((tl - base) & 0xFFFF) for _, tl in bs)
            print('%s  #%-5d t=%-10d %-9s len=%-3d %s' % (ts, idx, tick, DIRN.get(dirn, '?%d' % dirn), cnt, hexs))
            print('%s                                       ms: %s' % (' ' * len(ts), gaps))
        else:
            print('%s  TYPE 0x%02X len=%d  %s' % (ts, rt, ln, body.hex(' ')))
        n += 1
        if limit and n >= limit:
            print('... (stopped after %d records)' % n); return
    print('--- %d records ---' % n)


if __name__ == '__main__':
    p = sys.argv[1] if len(sys.argv) > 1 else r'C:\igs\GSA SAS Host Simulator v1.1\SASHostLog00000.log'
    lim = int(sys.argv[2]) if len(sys.argv) > 2 else None
    decode(p, lim)
