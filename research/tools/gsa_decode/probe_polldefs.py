import sys, re, struct
p = sys.argv[1] if len(sys.argv)>1 else r'C:\igs\GSA SAS Host Simulator v1.1\PollDefsSet1.dat'
d = open(p,'rb').read()
print("file=%s size=%d ver=%d count=%d" % (p, len(d), struct.unpack_from('<H',d,0)[0], struct.unpack_from('<I',d,2)[0]))
# find printable names preceded by a length byte
names=[]
for m in re.finditer(rb'[\x20-\x7e]{4,}', d):
    s,e = m.start(), m.end()
    ln = d[s-1]
    if ln == e-s:
        names.append((s-1, ln, d[s:e].decode('latin1')))
prev_end=6
for i,(off,ln,nm) in enumerate(names[:14]):
    hdr = d[prev_end:off+1]
    print("\n[%d] name @0x%X len=%d %r" % (i, off, ln, nm))
    print("   header(%d bytes from 0x%X): %s" % (len(hdr), prev_end, hdr.hex(' ')))
    prev_end = off+1+ln
print("\n... total names found: %d" % len(names))
