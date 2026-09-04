"""Decoders for the small fixed-shape .dat files of GSA SAS Host Simulator v1.1.

  ComPortSettings.dat   -- the GUI<->Engine serial link parameters
  TestConfigFile.dat    -- which TestData/*.dat file is currently loaded
  Tracefilters.dat      -- the named trace filters (Trace/Log -> Define Trace Filter)
  TicketList.dat        -- the ticket database (Configure -> Ticket Database)
"""
import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gsa_common import R, load, hexdump

PARITY = {0: 'None', 1: 'Odd', 2: 'Even', 3: 'Mark', 4: 'Space'}
STOPBITS = {0: '1', 1: '1.5', 2: '2'}


def comport(path):
    """ComPortSettings.dat (14 bytes)

        WORD    version   (1)
        CSTRING portName  ("COM3")
        DWORD   baudRate  (0x0001C200 = 115200)
        BYTE    byteSize  (8)
        BYTE    parity    (0 = none)
        BYTE    stopBits  (0 = one stop bit)

    Matches the DCB the executable programs at .text:0x0046299C after
    GetCommState (BaudRate=0x1C200, ByteSize=8, Parity=0, StopBits=0,
    DTR and RTS enabled, no flow control).
    """
    r = R(load(path))
    out = dict(version=r.u16(), port=r.cstr(), baud=r.u32(),
               bytesize=r.u8(), parity=r.u8(), stopbits=r.u8())
    print('=== %s ===' % path)
    print('  version   : %d' % out['version'])
    print('  port      : %s' % out['port'])
    print('  baud      : %d' % out['baud'])
    print('  framing   : %d%s%s  (bytesize=%d parity=%d stopbits=%d)' % (
        out['bytesize'], PARITY.get(out['parity'], '?')[0],
        STOPBITS.get(out['stopbits'], '?'), out['bytesize'], out['parity'], out['stopbits']))
    print('  trailing  : %s' % (r.blob(r.left()).hex(' ') or '(none)'))
    return out


def testconfig(path):
    """TestConfigFile.dat

        WORD    version  (0x0014 = 20)
        CSTRING fileName (the TestData\*.dat currently selected in the GUI)
    """
    r = R(load(path))
    v = r.u16(); n = r.cstr()
    print('=== %s ===' % path)
    print('  version   : %d' % v)
    print('  test file : %r' % n)
    return n


def tracefilters(path):
    """Tracefilters.dat

        WORD  version (1)
        DWORD filterCount
        filter := WORD 0x0002        (record tag)
                  CSTRING name
                  BYTE  kind         (1 in every shipped/observed filter)
                  DWORD codeCount
                  BYTE  pollCode[codeCount]

    A filter shows only the listed poll/exception codes in the SAS Trace window.
    """
    d = load(path); r = R(d)
    ver = r.u16(); n = r.u32()
    print('=== %s ===' % path)
    print('version=%d  filters=%d' % (ver, n))
    for i in range(n):
        tag = r.u16(); name = r.cstr(); kind = r.u8(); cnt = r.u32()
        codes = [r.u8() for _ in range(cnt)]
        print('  [%d] tag=%d kind=%d %-22s %2d codes: %s' % (
            i, tag, kind, '"%s"' % name, cnt, ' '.join('%02X' % c for c in codes)))
    if r.left():
        print('  trailing %d bytes: %s' % (r.left(), r.blob(r.left()).hex(' ')))


def ticketlist(path):
    """TicketList.dat  --  Configure -> Ticket Database ("Tickets In System")

        WORD  version (1)
        DWORD ticketCount
        ticket := WORD    0x0002        (record tag)
                  CSTRING ticketNumber  (validation number, normally 16 digits)
                  CSTRING systemId      ("00")
                  BYTE    ticketType    (1 = cashable in all shipped data)
                  BYTE    redeemed      (0 / 1)
                  double  amount        (IEEE-754 LE, currency units)
                  BYTE    flag          (1 on the shipped sample, 0 on user rows)
                  CSTRING expiration    (8 chars: MMDDYYYY, or a day count)
                  WORD    restrictedPoolId
    """
    d = load(path); r = R(d)
    ver = r.u16(); n = r.u32()
    print('=== %s ===' % path)
    print('version=%d  tickets=%d' % (ver, n))
    for i in range(n):
        tag = r.u16(); val = r.cstr(); sysid = r.cstr()
        ttype = r.u8(); redeemed = r.u8(); amt = r.f64()
        flag = r.u8(); exp = r.cstr(); pool = r.u16()
        print('  [%2d] number=%-20r sysId=%-5r type=%d redeemed=%d amount=%-10g flag=%d expiry=%-10r pool=%d'
              % (i, val, sysid, ttype, redeemed, amt, flag, exp, pool))
    if r.left():
        print('  trailing %d bytes: %s' % (r.left(), r.blob(r.left()).hex(' ')))


if __name__ == '__main__':
    base = sys.argv[1] if len(sys.argv) > 1 else r'C:\igs\GSA SAS Host Simulator v1.1'
    comport(os.path.join(base, 'ComPortSettings.dat')); print()
    testconfig(os.path.join(base, 'TestConfigFile.dat')); print()
    tracefilters(os.path.join(base, 'Tracefilters.dat')); print()
    ticketlist(os.path.join(base, 'TicketList.dat'))
