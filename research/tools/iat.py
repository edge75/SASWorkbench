import struct
d=open(r'C:\IGT3\sastest.exe','rb').read()
e=struct.unpack_from('<I',d,0x3c)[0]; coff=e+4; opt=coff+20
nsec=struct.unpack_from('<H',d,coff+2)[0]; optsz=struct.unpack_from('<H',d,coff+16)[0]
secs=[]
so=opt+optsz
for i in range(nsec):
    b=d[so+i*40:so+i*40+40]; nm=b[:8].rstrip(b'\0').decode()
    vsz,va,rsz,ptr=struct.unpack_from('<IIII',b,8); secs.append((nm,va,vsz,ptr,rsz))
def r2o(r):
    for nm,va,vsz,ptr,rsz in secs:
        if va<=r<va+max(vsz,rsz): return ptr+(r-va)
def cstr(o):
    return d[o:d.index(b'\0',o)].decode('latin1')
ddoff=opt+96
imp=struct.unpack_from('<I',d,ddoff+8)[0]; o=r2o(imp)
iat={}
i=0
while True:
    ent=d[o+i*20:o+i*20+20]
    if len(ent)<20 or ent==b'\0'*20: break
    olt,ts,fc,nr,fta=struct.unpack('<IIIII',ent)
    dll=cstr(r2o(nr))
    t=r2o(olt if olt else fta); j=0
    while True:
        v=struct.unpack_from('<I',d,t+j*4)[0]
        if v==0: break
        nm = f'#ord{v&0xffff}' if v&0x80000000 else cstr(r2o(v&0x7fffffff)+2)
        iat[0x400000+fta+j*4]=(dll,nm)
        j+=1
    i+=1
WANT={'SetCommState','BuildCommDCBA','CreateFileA','SetCommTimeouts','SetupComm','PurgeComm','WaitCommEvent','ClearCommError','WriteFile','ReadFile','SetCommMask','GetOverlappedResult','timeSetEvent','timeBeginPeriod','Sleep','GetTickCount','SetThreadPriority','SetPriorityClass','GetPrivateProfileStringA','CreateThread','WaitForSingleObject'}
rev={}
for a,(dll,nm) in iat.items():
    if nm in WANT: rev[a]=nm
# find "jmp dword ptr [addr]" thunks (ff 25 <addr>)
thunks={}
for a,nm in rev.items():
    pat=b'\xff\x25'+struct.pack('<I',a)
    i=0
    while True:
        i=d.find(pat,i)
        if i<0:break
        thunks[i+0x400a00]=nm   # VA of the thunk
        i+=1
print("=== thunks ===")
for va,nm in sorted(thunks.items()): print("  0x%08x  %s" % (va,nm))
# now find all "call rel32" targeting thunks, and direct "call dword ptr[iat]"
print("\n=== call sites ===")
res=[]
for i in range(0x600, 0xafe00+0x600-5):
    if d[i]==0xe8:
        rel=struct.unpack_from('<i',d,i+1)[0]
        tgt=(i+0x400a00)+5+rel
        if tgt in thunks: res.append((i+0x400a00, thunks[tgt]))
    elif d[i]==0xff and d[i+1]==0x15:
        a=struct.unpack_from('<I',d,i+2)[0]
        if a in rev: res.append((i+0x400a00, rev[a]))
for va,nm in res:
    print("  0x%08x  call %s" % (va,nm))
