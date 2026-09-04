import struct, re, sys
d = open(r"C:\IGT3\SASTEST.HLP",'rb').read()
def fread(name):
    i = d.find(name+b'\x00')
    off = struct.unpack_from('<i', d, i+len(name)+1)[0]
    rs, us, ff = struct.unpack_from('<iiB', d, off)
    return d[off+9: off+9+us]

# ---- |TTLBTREE : topic titles (btree of TopicOffset -> title string) ----
tt = fread(b'|TTLBTREE')
magicbt, flags, pagesize = struct.unpack_from('<HHH', tt, 0)
mustbezero,pagesplits,rootpage,neg1,totalpages,nlevels,totalentries = struct.unpack_from('<hhhhhhi', tt, 22)
sys.stderr.write("TTLBTREE pagesize=%d pages=%d levels=%d entries=%d\n"%(pagesize,totalpages,nlevels,totalentries))
titles=[]
pages = tt[38:]
for pg in range(totalpages):
    p = pages[pg*pagesize:(pg+1)*pagesize]
    if len(p)<6: break
    nent, prev, nxt = struct.unpack_from('<hhh', p, 0)
    q=6
    for i in range(nent):
        if q+4>len(p): break
        toff = struct.unpack_from('<i', p, q)[0]; q+=4
        e = p.find(b'\x00', q)
        if e<0: break
        titles.append((toff, p[q:e].decode('latin-1')))
        q = e+1
print("="*70)
print("TOPIC TITLES from |TTLBTREE  (help table of contents), %d entries" % len(titles))
print("="*70)
for toff,t in titles:
    print("  TopicOffset 0x%08X  %s" % (toff & 0xffffffff, t))

# ---- |CONTEXT : hash -> topic offset (context strings not stored, only hashes) ----
# ---- |TOPIC : uncompressed topic blocks ----
tp = fread(b'|TOPIC')
print()
print("="*70)
print("|TOPIC raw size = %d bytes (SYSTEM flags=0 => UNCOMPRESSED)" % len(tp))
print("="*70)
BLOCK = 4096
out=[]
nb = (len(tp)+BLOCK-1)//BLOCK
for b in range(nb):
    blk = tp[b*BLOCK:(b+1)*BLOCK]
    if len(blk) < 12: continue
    lastlink, firstlink, lastheader = struct.unpack_from('<iii', blk, 0)
    out.append(("BLOCK %d lastlink=%d firstlink=%d lastheader=%d" % (b,lastlink,firstlink,lastheader), blk[12:]))
open(r"C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research\topic_raw.bin","wb").write(tp)
