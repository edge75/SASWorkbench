r"""HostConfigs.dat and TestData\*.dat  --  "Test Configuration Templates".

Both files use the same container.  Each record is one entry of the main
window's "Test Number" combo.  HostConfigs.dat holds the two global entries;
each TestData\<group>.dat holds the templates of one test group named in
configFiles.txt / configDesc.txt.

    file   := WORD  fileVersion (0x000B = 11)
              DWORD configCount
              config[configCount]

    config := WORD    0x000B          (record tag, equals the file version)
              CSTRING name            ("Current", "16.01.02", "Required Accounting", ...)
              settings                (variable length, 373..770 bytes)

    settings :=
        WORD    pollRateMs                 SAS Comms: "Poll rate (ms)"
        WORD    responseToNextPollMs       SAS Comms: "Time from response to next poll"
        BYTE[9] flagsA                     SAS Comms + ACK/NACK check boxes
        WORD    nackCount                  ACK/NACK: "Number of times to NACK"
        WORD    resendDelayMs              ACK/NACK: "Delay between bad response and resend"
        BYTE[9] flagsB                     Tickets page check boxes
        DWORD   delay1                     Tickets page delays (ms)
        DWORD   delay2
        CSTRING validationSystemId         Tickets: "Validation System ID"
        CSTRING validationNumber           Tickets: "Validation Number"
        BYTE[12] reserved (zero)
        DWORD   delay3
        WORD    reserved
        DWORD   delay4
        BYTE[3] reserved (zero)
        32 x {  CSTRING levelAmount        Progressives page, levels 1..32
                CSTRING incrementRate
                BYTE    enabled }
        CSTRING comPort                    the port saved with this template
        8  x {  DWORD pollRef      the eight one-key (F1..F8) slots.
                                     pollRef = (pollCode << 8) | instanceIndex,
                                     e.g. 46337 = 0xB501 = poll B5 definition 1;
                                     0 = slot empty
                DWORD egmAddress     EGM address the key sends to (1 by default)
                DWORD reserved }
        DWORD   sel1                       combo-box selections (loop / poll set /
        DWORD   sel2                        test number / test group); -3 and -1
        DWORD   sel3                        occur as "none"
        DWORD   sel4
        BYTE[22] reserved (zero)
        CSTRING placeholder                ("dummy" in every shipped record)
        BYTE[5] reserved (zero)

Verified: all 49 shipped config files (live + Defaults) parse to exactly EOF.
"""
import sys, os, struct, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import R, load


def parse(path):
    r = R(load(path))
    ver = r.u16(); n = r.u32()
    cfgs = []
    for _ in range(n):
        tag = r.u16(); name = r.cstr()
        start = r.o
        c = dict(tag=tag, name=name)
        c['pollRateMs'] = r.u16(); c['responseToNextPollMs'] = r.u16()
        c['flagsA'] = r.blob(9)
        c['nackCount'] = r.u16(); c['resendDelayMs'] = r.u16()
        c['flagsB'] = r.blob(9)
        c['delay1'] = r.u32(); c['delay2'] = r.u32()
        c['validationSystemId'] = r.cstr(); c['validationNumber'] = r.cstr()
        r.blob(12)
        c['delay3'] = r.u32(); r.u16(); c['delay4'] = r.u32(); r.blob(3)
        c['levels'] = [(r.cstr(), r.cstr(), r.u8()) for _ in range(32)]
        c['comPort'] = r.cstr()
        c['oneKey'] = [(r.u32(), r.u32(), r.u32()) for _ in range(8)]
        c['sel'] = (r.i32(), r.i32(), r.i32(), r.i32())
        r.blob(22)
        c['placeholder'] = r.cstr()
        r.blob(5)
        c['size'] = r.o - start
        cfgs.append(c)
    return dict(version=ver, configs=cfgs, consumed=r.o, size=len(r.d))


def main(path):
    p = parse(path)
    print('=== %s ===' % path)
    print('version=%d  configs=%d  parsed %d of %d bytes' % (
        p['version'], len(p['configs']), p['consumed'], p['size']))
    for c in p['configs']:
        lv = [(i + 1, a, inc) for i, (a, inc, e) in enumerate(c['levels']) if e or (a not in ('0', ''))]
        print('\n  "%s"  (%d bytes)' % (c['name'], c['size']))
        print('     pollRate=%dms  respToNextPoll=%dms  nack=%d  resendDelay=%dms  port=%s'
              % (c['pollRateMs'], c['responseToNextPollMs'], c['nackCount'],
                 c['resendDelayMs'], c['comPort']))
        print('     validation: systemId=%r number=%r' % (c['validationSystemId'], c['validationNumber']))
        print('     delays: %d %d %d %d ms' % (c['delay1'], c['delay2'], c['delay3'], c['delay4']))
        print('     flagsA=%s  flagsB=%s' % (c['flagsA'].hex(' '), c['flagsB'].hex(' ')))
        print('     selections=%s' % (c['sel'],))
        keys = []
        for i, (ref, addr, z) in enumerate(c['oneKey'], 1):
            keys.append('F%d=%s' % (i, '-' if ref == 0 else '%02X.%d@%d' % (ref >> 8, ref & 0xFF, addr)))
        print('     one-key: %s' % ' '.join(keys))
        if lv:
            print('     progressive levels: %s' % ', '.join('L%d=%s(+%s)' % x for x in lv))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1
         else r'C:\igs\GSA SAS Host Simulator v1.1\HostConfigs.dat')
