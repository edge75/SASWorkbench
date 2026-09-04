"""PollDefsSet1/2/3.dat and UserPollDefinition.dat -- the stored poll library.

Verified layout (little-endian, ANSI text):

    file   := WORD  fileVersion            (always 1)
              DWORD groupCount
              group[groupCount]            (ordered by ascending pollCode)

    group  := BYTE  pollCode               (SAS long-poll command byte)
              BYTE  instanceCount          (may be 0 -- e.g. BF, CF in Set1)
              BYTE[3] 00 00 00             (pad / reserved)
              record[instanceCount]

    record := WORD  0x0005                 (record struct tag, constant)
              WORD  0x0001                 (record struct sub-version, constant)
              CSTRING name                 ("%02X - <text>"; may be zero-length)
              BYTE  instanceIndex          (1-based within the group)
              BYTE  pollCode               (always == the group's pollCode --
                                            verified across all four files)
              BYTE  dataLen
              BYTE  data[dataLen]          (the SAS message WITHOUT the address
                                            byte and WITHOUT the CRC:
                                            data[0] == command byte, then the
                                            encoded parameter bytes)
              BYTE  crcMode                (0 = No CRC, 1 = Valid CRC;
                                            2 = Invalid CRC is the third radio
                                            button, absent from shipped data)
              BYTE[] tail                  (26..35 bytes, variable; always
                                            begins 00 00 00 <DWORD> <DWORD> and
                                            contains the constant marker
                                            02 00 01 00.  Holds the multi-denom
                                            flag + denom index, the interbyte
                                            delay list and the wakeup-bit list.
                                            NOT fully decoded.)

A polling-loop entry and a one-key (F1..F8) slot reference a poll by the pair
(pollCode, instanceIndex) -- see decode_loopdefs.py.

The application exports the readable form of every parameterised poll to
PollDef.txt, so the tail encoding is not needed to read poll content.
"""
import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import load

MARK = bytes([5, 0, 1, 0])
CRC = {0: 'no CRC', 1: 'valid CRC', 2: 'invalid CRC', 0xFF: 'CRC unset (FF)'}


def _plausible(d, i):
    """True if a 05 00 01 00 tag at offset i is followed by a real record."""
    if i + 5 > len(d):
        return False
    ln = d[i + 4]
    nm = d[i + 5:i + 5 + ln]
    if len(nm) != ln or (ln and not all(32 <= c < 127 for c in nm)):
        return False
    o = i + 5 + ln
    if o + 3 > len(d):
        return False
    idx, pc, dl = d[o], d[o + 1], d[o + 2]
    if not (1 <= idx <= 64):
        return False
    if o + 3 + dl >= len(d):
        return False
    if dl and d[o + 3] != pc:          # data[0] is always the command byte
        return False
    return d[o + 3 + dl] in (0, 1, 2, 0xFF)  # crcMode


def _record_marks(d):
    """All byte offsets at which a record's 05 00 01 00 tag starts."""
    out = []
    i = d.find(MARK)
    while i != -1:
        if _plausible(d, i):
            out.append(i)
        i = d.find(MARK, i + 1)
    return out


def parse(path):
    d = load(path)
    ver = struct.unpack_from('<H', d, 0)[0]
    ngroups = struct.unpack_from('<I', d, 2)[0]
    marks = _record_marks(d)
    mi = 0
    o = 6
    groups = []
    g = 0
    while g < ngroups:
        code = d[o]; ninst = d[o + 1]; o += 5      # 5-byte group header
        recs = []
        for i in range(ninst):
            assert d[o:o + 4] == MARK, 'bad record tag, group %d(%02X) rec %d @0x%X: %s' % (
                g, code, i, o, d[o:o + 12].hex(' '))
            mi += 1
            o += 4
            ln = d[o]; name = d[o + 1:o + 1 + ln].decode('latin-1'); o += 1 + ln
            idx = d[o]; pc = d[o + 1]; dl = d[o + 2]
            data = d[o + 3:o + 3 + dl]; crc = d[o + 3 + dl]; o += 4 + dl
            nxt = marks[mi] if mi < len(marks) else len(d)
            if i + 1 < ninst:
                end = nxt
            else:
                # one or more 5-byte group headers sit between this record's
                # tail and the next record; codes ascend and every skipped
                # group has instanceCount 0.
                k = 1
                while True:
                    p = nxt - 5 * (k + 1)
                    if p <= o: break
                    if d[p + 1] != 0: break                # earlier groups must be empty
                    if d[p + 2:p + 5] != b'\0\0\0': break  # pad must be zero
                    if d[p] >= d[nxt - 5]: break           # codes ascend
                    if d[p] <= code: break
                    k += 1
                if nxt < len(d):
                    end = nxt - 5 * k
                else:
                    end = len(d) - 5 * (ngroups - g - 1)
            recs.append(dict(name=name, index=idx, code=pc, data=data,
                             crc=crc, tail=d[o:end]))
            o = end
        groups.append(dict(code=code, ninst=ninst, recs=recs))
        g += 1
    return dict(version=ver, groups=groups, consumed=o, size=len(d), marks=len(marks))


def main(path, verbose=False):
    r = parse(path)
    total = sum(g['ninst'] for g in r['groups'])
    print('=== %s ===' % path)
    print('fileVersion=%d  groups=%d  polls=%d  (record tags found: %d)  parsed 0x%X of 0x%X bytes'
          % (r['version'], len(r['groups']), total, r['marks'], r['consumed'], r['size']))
    for g in r['groups']:
        print('\nLP %02X  (%d definition%s)' % (g['code'], g['ninst'], '' if g['ninst'] == 1 else 's'))
        for rec in g['recs']:
            print('   [%02X.%d] %-52s data=%-40s %s' % (
                g['code'], rec['index'], '"%s"' % rec['name'],
                rec['data'].hex(' ') or '(none)', CRC.get(rec['crc'], '?%d' % rec['crc'])))
            if verbose:
                print('           tail(%d)=%s' % (len(rec['tail']), rec['tail'].hex(' ')))


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    p = args[0] if args else r'C:\igs\GSA SAS Host Simulator v1.1\PollDefsSet1.dat'
    main(p, '-v' in sys.argv)
