import struct, sys
d = open(r"C:\IGT3\SASTEST.HLP",'rb').read()
def fread(name):
    i = d.find(name+b'\x00')
    off = struct.unpack_from('<i', d, i+len(name)+1)[0]
    rs, us, ff = struct.unpack_from('<iiB', d, off)
    return d[off+9: off+9+us]

tt = fread(b'|TTLBTREE')
print("="*72); print("TOPIC TITLES (|TTLBTREE) -- help table of contents"); print("="*72)
# just pull null-terminated printable strings preceded by a 4-byte offset
i=38
seen=[]
while i < len(tt):
    j=i
    while j<len(tt) and 32 <= tt[j] < 127: j+=1
    if j-i >= 3 and j<len(tt) and tt[j]==0:
        s = tt[i:j].decode('latin-1')
        toff = struct.unpack_from('<I', tt, i-4)[0] if i>=4 else 0
        seen.append((toff,s)); i=j+1
    else: i+=1
for t,s in seen: print("  0x%08X  %s" % (t,s))

# ---------- |TOPIC ----------
tp = fread(b'|TOPIC')
BLOCK=4096
raw = bytearray()
for b in range(0, len(tp), BLOCK):
    raw += tp[b+12 : b+BLOCK]      # strip 12-byte block header (uncompressed => no decompress)
print(); print("="*72); print("|TOPIC decoded (uncompressed, %d payload bytes)" % len(raw)); print("="*72)

out=[]
p=0
n=len(raw)
while p+21 <= n:
    blocksize, datalen2 = struct.unpack_from('<ii', raw, p)
    prevb, nextb = struct.unpack_from('<ii', raw, p+8)
    rtype = raw[p+16]
    if blocksize <= 0 or blocksize > n or datalen2 < 0:
        p += 1; continue
    if rtype not in (0x02, 0x20, 0x23):
        p += 1; continue
    hdr = p+17
    if rtype == 0x02:   # TOPICHEADER
        out.append(("\n\n" + "#"*60 + "\n"))
        # topic header: 4 int + ...
        p = p + max(blocksize, 1)
        continue
    # 0x20 (DISPLAYABLE) / 0x23 (TABLE): skip the paragraph-info, then text follows
    # Text is stored right after the record header for uncompressed files at offset p+17+... 
    p = p + max(blocksize, 1)
# simpler & robust: strip formatting, keep printable runs in file order
txt=[]
cur=bytearray()
for ch in raw:
    if 32 <= ch < 127 or ch in (9,):
        cur.append(ch)
    else:
        if len(cur) >= 2:
            txt.append(cur.decode('latin-1'))
        elif len(cur)==1 and cur[0] in b'$?@;=|':
            txt.append(cur.decode('latin-1'))
        cur=bytearray()
        if ch in (0x0d,0x0a): txt.append("\n")
if len(cur)>=2: txt.append(cur.decode('latin-1'))
res = "".join(txt)
print(res)
