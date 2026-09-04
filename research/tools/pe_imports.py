import struct, sys
p = sys.argv[1]
d = open(p,'rb').read()
e_lfanew = struct.unpack_from('<I', d, 0x3c)[0]
assert d[e_lfanew:e_lfanew+4] == b'PE\0\0', 'not PE'
coff = e_lfanew+4
nsec, = struct.unpack_from('<H', d, coff+2)
optsz, = struct.unpack_from('<H', d, coff+16)
opt = coff+20
magic, = struct.unpack_from('<H', d, opt)
pe32 = (magic == 0x10b)
imagebase = struct.unpack_from('<I' if pe32 else '<Q', d, opt+28)[0]
ddoff = opt + (96 if pe32 else 112)
nrva, = struct.unpack_from('<I', d, opt+(92 if pe32 else 108))
print(f'PE32={pe32} imagebase=0x{imagebase:x} sections={nsec} numDataDir={nrva}')
secs=[]
so = opt+optsz
for i in range(nsec):
    b = d[so+i*40: so+i*40+40]
    name=b[0:8].rstrip(b'\0').decode('latin1')
    vsz,va,rsz,ptr = struct.unpack_from('<IIII', b, 8)
    secs.append((name,va,vsz,ptr,rsz))
    print(f'  sec {name:10s} VA=0x{va:08x} VSZ=0x{vsz:06x} RAW=0x{ptr:08x} RSZ=0x{rsz:06x}')
def r2o(rva):
    for name,va,vsz,ptr,rsz in secs:
        if va <= rva < va+max(vsz,rsz):
            return ptr + (rva-va)
    return None
DIRS=['Export','Import','Resource','Exception','Certificate','BaseReloc','Debug','Arch','GlobalPtr','TLS','LoadConfig','BoundImport','IAT','DelayImport','CLR','Rsvd']
for i in range(min(nrva,16)):
    rva,sz = struct.unpack_from('<II', d, ddoff+i*8)
    if rva: print(f'  DD[{i}] {DIRS[i]:12s} RVA=0x{rva:08x} size=0x{sz:x}')
imp_rva, imp_sz = struct.unpack_from('<II', d, ddoff+8)
o = r2o(imp_rva)
print('\n=== IMPORTS ===')
def cstr(off):
    end = d.index(b'\0', off); return d[off:end].decode('latin1')
i=0
while True:
    ent = d[o+i*20:o+i*20+20]
    if len(ent)<20 or ent==b'\0'*20: break
    olt,ts,fc,nrva_,fta = struct.unpack('<IIIII', ent)
    dll = cstr(r2o(nrva_))
    thunks=[]
    t = r2o(olt if olt else fta)
    j=0
    while True:
        v, = struct.unpack_from('<I', d, t+j*4)
        if v==0: break
        if v & 0x80000000: thunks.append(f'#ord{v & 0xffff}')
        else:
            ho = r2o(v & 0x7fffffff)
            thunks.append(cstr(ho+2))
        j+=1
    print(f'\n--- {dll}  ({len(thunks)} imports) ---')
    print('  ' + ', '.join(sorted(thunks)))
    i+=1
# delay imports
drva,dsz = struct.unpack_from('<II', d, ddoff+13*8)
print('\nDelayImport RVA=0x%x size=0x%x' % (drva,dsz))
