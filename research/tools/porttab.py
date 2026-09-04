import struct
d=open(r'C:\IGT3\sastest.exe','rb').read()
# .data VA 0xB1000 rawptr 0xB0400 ; imagebase 0x400000
def va2off(va):
    rva = va-0x400000
    if 0xB1000 <= rva < 0xC5000: return rva-0xB1000+0xB0400
    if 0x1000 <= rva < 0xB1000: return rva-0x1000+0x600
    return None
tab = 0xb761c - 8*4 - 4   # walk back over the 8 pointers
print("pointer array region (file offset 0x%x):" % tab)
for i in range(10):
    o = 0xb761c-40+i*4
    v = struct.unpack_from('<I', d, o)[0]
    off = va2off(v)
    s=''
    if off and 0 < off < len(d):
        e=d.find(b'\0', off)
        if 0 < e-off < 40:
            s = repr(d[off:e])
    print("  fileoff 0x%06x -> VA 0x%08X  off=%s  %s" % (o, v, hex(off) if off else '-', s))
print()
print("--- string right after COM table region: the 3 DCB strings ---")
p = d.find(b'baud=19200')
while p>=0 and p < d.find(b'baud=19200')+400:
    e=d.find(b'\0',p)
    print("  0x%06x  %r" % (p, d[p:e]))
    n = d.find(b'baud=19200', e)
    if n<0 or n>p+400: break
    p=n
print()
print("--- 'Could not create Comm Port' / 'BuildCommPort failed!' context ---")
for s in [b'Could not create Comm Port', b'BuildCommPort failed!', b'TIGTComm', b'Wakeup Delay']:
    i=d.find(s)
    print("  %-30r at 0x%06x" % (s, i))
