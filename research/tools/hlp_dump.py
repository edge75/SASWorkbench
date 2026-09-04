import sys, struct
p = r"C:\IGT3\SASTEST.HLP"
d = open(p,'rb').read()
magic, dirstart, nonused, total = struct.unpack_from('<iiii', d, 0)
print("magic=0x%08X dirstart=%d free=%d filesize=%d actual=%d" % (magic, dirstart, nonused, total, len(d)))
# WinHelp internal file header at offset: reservedspace(4) usedspace(4) fileflags(1)
def fhdr(off):
    rs, us, ff = struct.unpack_from('<iiB', d, off)
    return rs, us, ff
rs, us, ff = fhdr(dirstart)
print("dir: reserved=%d used=%d flags=%d" % (rs,us,ff))
# B+ tree header follows
bt = dirstart + 9
magicbt, flags, pagesize = struct.unpack_from('<HHH', d, bt)
structfmt = d[bt+6:bt+22]
print("btree magic=0x%04X flags=0x%04X pagesize=%d struct=%r" % (magicbt, flags, pagesize, structfmt))
mustbezero, pagesplits, rootpage, mustbeneg1, totalpages, nlevels, totalentries = struct.unpack_from('<hhhhhhi', d, bt+22)
print("rootpage=%d totalpages=%d nlevels=%d entries=%d" % (rootpage, totalpages, nlevels, totalentries))
pagesstart = bt+38
# walk leaf pages
files={}
for pg in range(totalpages):
    off = pagesstart + pg*pagesize
    nent, prev, nxt = struct.unpack_from('<hhh', d, off)
    q = off+6
    ok=True
    ents=[]
    for i in range(nent):
        e = d.index(b'\x00', q)
        name = d[q:e].decode('latin-1')
        q = e+1
        if q+4 > off+pagesize: ok=False; break
        fo = struct.unpack_from('<i', d, q)[0]; q+=4
        ents.append((name,fo))
    if ok:
        for n,o in ents: files[n]=o
for n,o in sorted(files.items(), key=lambda x:x[1]):
    r,u,f = fhdr(o)
    print("  %-14s off=%-8d reserved=%-8d used=%-8d flags=%d" % (n,o,r,u,f))
