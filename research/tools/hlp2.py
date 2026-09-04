import struct
d = open(r"C:\IGT3\SASTEST.HLP",'rb').read()
# find internal file names literally
for nm in [b'|CONTEXT',b'|CTXOMAP',b'|FONT',b'|SYSTEM',b'|TOPIC',b'|TTLBTREE',b'|Phrases',b'|PhrIndex',b'|Rose',b'|bm']:
    i=0
    while True:
        i = d.find(nm+b'\x00', i)
        if i<0: break
        off = struct.unpack_from('<i', d, i+len(nm)+1)[0]
        if 0 < off < len(d):
            rs,us,ff = struct.unpack_from('<iiB', d, off)
            print("%-12s dirent@%d -> off=%d reserved=%d used=%d flags=%d" % (nm.decode(),i,off,rs,us,ff))
        i += 1
print("--- |SYSTEM header ---")
so = None
i = d.find(b'|SYSTEM\x00')
so = struct.unpack_from('<i', d, i+8)[0]
h = so+9
magic, minor, major = struct.unpack_from('<HHH', d, h)
print("sys magic=0x%04X ver minor=%d major=%d" % (magic,minor,major))
gendate, flags = struct.unpack_from('<iH', d, h+6)
print("gendate=%d flags=%d  (0=no compress,4=LZ77,8=LZ77+phrase)" % (gendate,flags))
q = h+12
while q < so+9+struct.unpack_from('<i',d,so+4)[0]-4:
    rt, dlen = struct.unpack_from('<HH', d, q)
    body = d[q+4:q+4+dlen]
    print("  rec type=%d len=%d %r" % (rt,dlen,body[:120]))
    q += 4+dlen
    if dlen==0 and rt==0: break
