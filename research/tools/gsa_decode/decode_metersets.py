"""MeterSetDefinitions.dat  --  Windows -> Meters -> "Select Meters to Display".

    file    := WORD  fileVersion (1)
               DWORD setCount
               set[setCount]

    set     := WORD    0x0001      (record tag)
               CSTRING name
               DWORD   gameNumber  (0 = EGM-level meters)
               DWORD   denomIndex  (0xFFFFFFFF = not specified)
               DWORD   meterCount
               WORD    meterCode[meterCount]   (SAS meter codes, e.g. 0x0000
                                                = total coin in credits)

Meter code names live in the executable's string table (IDs 62001.. and
63001..), e.g. 0x0000 "Coin In", 0x0024 "Current Credits".
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import R, load


def parse(path):
    r = R(load(path))
    ver = r.u16(); n = r.u32()
    sets = []
    for _ in range(n):
        tag = r.u16(); name = r.cstr(); game = r.u32(); denom = r.i32(); cnt = r.u32()
        codes = [r.u16() for _ in range(cnt)]
        sets.append(dict(tag=tag, name=name, game=game, denom=denom, codes=codes))
    return dict(version=ver, sets=sets, left=r.left())


def main(path):
    d = parse(path)
    print('=== %s ===' % path)
    print('version=%d  meterSets=%d  unparsed tail=%d' % (d['version'], len(d['sets']), d['left']))
    for s in d['sets']:
        print('  %-32s game=%-4d denom=%-4s %2d meters: %s' % (
            '"%s"' % s['name'], s['game'],
            'any' if s['denom'] == -1 else s['denom'], len(s['codes']),
            ' '.join('%04X' % c for c in s['codes'])))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1
         else r'C:\igs\GSA SAS Host Simulator v1.1\MeterSetDefinitions.dat')
