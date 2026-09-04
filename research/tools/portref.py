import struct
d=open(r'C:\IGT3\sastest.exe','rb').read()
IB=0x400000
def off2va(o):
    if 0x600 <= o < 0xb0400: return o-0x600+0x1000+IB
    if 0xb0400 <= o < 0xbea00: return o-0xb0400+0xb1000+IB
    return None
def va2off(va):
    r=va-IB
    if 0x1000<=r<0xb1000: return r-0x1000+0x600
    if 0xb1000<=r<0xc5000: return r-0xb1000+0xb0400
    return None

# wider dump of the pointer region
print("=== pointer region around COM table (file 0x0b75c0 - 0x0b7620) ===")
for o in range(0xb75c0, 0xb7624, 4):
    v=struct.unpack_from('<I', d, o)[0]
    t=va2off(v)
    s=''
    if t and 0<t<len(d):
        e=d.find(b'\0',t)
        if 0 < e-t < 30 and all(32<=c<127 for c in d[t:e]):
            s=repr(d[t:e])
    print("  off=0x%06x VA=0x%08X  %s" % (o, v, s))

print()
print("=== code refs to the table base and its members ===")
for va in (0x4B81FC, 0x4B821C, 0x4B8222, 0x4B824C, 0x4B826E, 0x4B8290, 0x4B82B2):
    pat = struct.pack('<I', va)
    hits=[]
    s=0
    while True:
        j=d.find(pat, s)
        if j<0: break
        if 0x600 <= j < 0xb0400:
            hits.append(hex(j))
        s=j+1
        if len(hits)>8: break
    lbl = {0x4B821C:'COM1: string', 0x4B824C:'DCB space-parity #1',
           0x4B826E:'DCB mark-parity', 0x4B8290:'DCB space-parity #2',
           0x4B82B2:'"Could not create Comm Port"'}.get(va,'')
    print("  VA 0x%08X %-30s code refs: %s" % (va, lbl, hits))

print()
print("=== disassembly-ish: bytes around each .text ref to 0x4B81FC ===")
pat=struct.pack('<I',0x4B81FC)
s=0
while True:
    j=d.find(pat,s)
    if j<0: break
    if 0x600<=j<0xb0400:
        print("  file 0x%06x (VA 0x%08X):" % (j, off2va(j)))
        print("     %s" % d[j-24:j+16].hex(' '))
    s=j+1
