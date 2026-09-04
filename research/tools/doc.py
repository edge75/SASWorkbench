import struct, re, sys
p = r"C:\IGT3\SASINI.doc"
d = open(p,'rb').read()
assert d[:8] == b'\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1', "not OLE2"
ssz = 1 << struct.unpack_from('<H', d, 30)[0]
sssz = 1 << struct.unpack_from('<H', d, 32)[0]
nfat = struct.unpack_from('<i', d, 44)[0]
dirstart = struct.unpack_from('<i', d, 48)[0]
minicut = struct.unpack_from('<i', d, 56)[0]
ministart = struct.unpack_from('<i', d, 60)[0]
difstart, ndif = struct.unpack_from('<ii', d, 68)
sys.stderr.write("sector=%d mini=%d nfat=%d dirstart=%d ministart=%d minicutoff=%d\n"%(ssz,sssz,nfat,dirstart,ministart,minicut))
def sec(n): off = 512 + n*ssz; return d[off:off+ssz]
# build FAT
fatsecs = [struct.unpack_from('<i', d, 76+4*i)[0] for i in range(min(nfat,109))]
nxt = difstart
while nxt not in (-2,-1) and len(fatsecs) < nfat:
    s = sec(nxt)
    for i in range(0, ssz-4, 4):
        v = struct.unpack_from('<i', s, i)[0]
        if v >= 0: fatsecs.append(v)
    nxt = struct.unpack_from('<i', s, ssz-4)[0]
FAT=[]
for fs in fatsecs:
    s = sec(fs)
    FAT += [struct.unpack_from('<i', s, i)[0] for i in range(0, ssz, 4)]
def chain(start):
    out=[]; c=start
    while c >= 0 and c < len(FAT) and len(out) < 100000:
        out.append(c); c = FAT[c]
    return out
def readstream(start, size):
    b = b''.join(sec(x) for x in chain(start))
    return b[:size] if size else b
# directory
dirdata = b''.join(sec(x) for x in chain(dirstart))
entries=[]
for i in range(0, len(dirdata), 128):
    e = dirdata[i:i+128]
    if len(e)<128: break
    nlen = struct.unpack_from('<H', e, 64)[0]
    if nlen<2: continue
    name = e[:nlen-2].decode('utf-16-le', 'replace')
    typ = e[66]
    sect, size = struct.unpack_from('<iI', e, 116)
    entries.append((name, typ, sect, size))
    print("DIR: %-24s type=%d sect=%d size=%d" % (name, typ, sect, size))
root = [e for e in entries if e[1]==5][0]
mini = readstream(root[2], root[3])
def readany(sect, size):
    if size < minicut:
        # mini stream
        out=b''; c=sect
        while c>=0 and c<len(FAT):
            out += mini[c*sssz:(c+1)*sssz]; c=FAT[c]
        return out[:size]
    return readstream(sect, size)
wd = None
for name,typ,sect,size in entries:
    if name == 'WordDocument': wd = readany(sect,size)
    if name.startswith('\x01Table') or name.endswith('Table'):
        globals()['TBL_'+name.strip('\x01')] = readany(sect,size)
print("WordDocument stream = %d bytes" % len(wd))
open(r"C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research\worddoc.bin",'wb').write(wd)
# FIB: fcMin at 0x18, fcMac at 0x1C (Word 6/95); Word97 uses piece table in Table stream
wIdent, nFib = struct.unpack_from('<HH', wd, 0)
flags = struct.unpack_from('<H', wd, 10)[0]
fWhichTblStm = (flags >> 9) & 1
print("wIdent=0x%04X nFib=%d fWhichTblStm=%d" % (wIdent, nFib, fWhichTblStm))
fcMin, fcMac = struct.unpack_from('<ii', wd, 24)
print("fcMin=%d fcMac=%d" % (fcMin,fcMac))
tblname = '\x01Table' if fWhichTblStm else '\x000Table'
tbl=None
for name,typ,sect,size in entries:
    if name.endswith('Table'): tbl = readany(sect,size); print("using table stream %r (%d bytes)"%(name,len(tbl)))
fcClx, lcbClx = struct.unpack_from('<iI', wd, 0x01A2)
print("fcClx=%d lcbClx=%d" % (fcClx, lcbClx))
clx = tbl[fcClx:fcClx+lcbClx]
# parse Clx -> find pcdt (0x02)
i=0
pieces=[]
while i < len(clx):
    t = clx[i]
    if t == 1:
        cb = clx[i+1]; i += 2+cb
    elif t == 2:
        lcb = struct.unpack_from('<I', clx, i+1)[0]
        pt = clx[i+5:i+5+lcb]
        n = (lcb-4)//12
        cps = [struct.unpack_from('<I', pt, 4*k)[0] for k in range(n+1)]
        for k in range(n):
            off = 4*(n+1) + 8*k
            fc = struct.unpack_from('<I', pt, off+2)[0]
            comp = (fc & 0x40000000) != 0
            fcv = fc & 0x3FFFFFFF
            pieces.append((cps[k], cps[k+1], fcv//2 if comp else fcv, comp))
        break
    else:
        break
print("pieces=%d" % len(pieces))
txt=[]
for cpS, cpE, fc, comp in pieces:
    n = cpE-cpS
    if comp:
        raw = wd[fc:fc+n]
        txt.append(raw.decode('cp1252','replace'))
    else:
        raw = wd[fc:fc+2*n]
        txt.append(raw.decode('utf-16-le','replace'))
s = "".join(txt)
s = s.replace('\r','\n').replace('\x07','\t|\t').replace('\x0c','\n--- PAGE BREAK ---\n').replace('\x0b','\n')
print("="*72); print("EXTRACTED TEXT (%d chars)" % len(s)); print("="*72)
print(s)
