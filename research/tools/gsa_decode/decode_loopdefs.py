"""LoopDefs1/2/3.dat, PollLoopDefinitions.dat, UserLoopDefs.dat  --  polling loops.

    file := WORD  fileVersion (1)
            DWORD loopCount
            loop[loopCount]

    loop := WORD    0x0001        (record tag)
            CSTRING name
            WORD    loopId        (1-based ordinal of the loop in the file)
            DWORD   entryCount
            entry[entryCount]

    entry:= WORD 0x0001           (entry tag)
            BYTE egmAddress       (0 = broadcast, 1..127 = EGM address)
            BYTE instanceIndex    (which definition of that poll code -- see
                                   decode_polldefs.py; 1-based)
            BYTE pollCode         (SAS long-poll command byte; 00 = general poll)

There is one loop file per poll-definition set:
    PollDefsSet1.dat       <-> LoopDefs1.dat
    PollDefsSet2.dat       <-> LoopDefs2.dat
    PollDefsSet3.dat       <-> LoopDefs3.dat
    UserPollDefinition.dat <-> UserLoopDefs.dat
PollLoopDefinitions.dat is the master/first-set copy the GUI actually runs.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import R, load


def parse(path):
    r = R(load(path))
    ver = r.u16(); n = r.u32()
    loops = []
    for _ in range(n):
        tag = r.u16(); name = r.cstr(); lid = r.u16(); cnt = r.u32()
        entries = []
        for _ in range(cnt):
            etag = r.u16(); addr = r.u8(); inst = r.u8(); code = r.u8()
            entries.append((etag, addr, inst, code))
        loops.append(dict(tag=tag, name=name, id=lid, entries=entries))
    return dict(version=ver, loops=loops, left=r.left())


def main(path):
    d = parse(path)
    print('=== %s ===' % path)
    print('version=%d  loops=%d  unparsed tail=%d' % (d['version'], len(d['loops']), d['left']))
    for lp in d['loops']:
        print('\n  loop #%-3d %-28s %d entries' % (lp['id'], '"%s"' % lp['name'], len(lp['entries'])))
        for etag, addr, inst, code in lp['entries']:
            print('        addr=%-3d poll=%02X inst=%-2d %s' % (
                addr, code, inst, '(general poll)' if code == 0 else ''))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1
         else r'C:\igs\GSA SAS Host Simulator v1.1\PollLoopDefinitions.dat')
