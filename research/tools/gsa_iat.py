import struct, sys, os
PATH = r"C:\igs\GSA SAS Host Simulator v1.1\SASHostSim.exe"
data = open(PATH,'rb').read()
u16=lambda o: struct.unpack_from('<H',data,o)[0]
u32=lambda o: struct.unpack_from('<I',data,o)[0]
e=u32(0x3C); coff=e+4; nsec=u16(coff+2); so=u16(coff+16); opt=coff+20; st=opt+so
secs=[]
for i in range(nsec):
    b=st+i*40
    secs.append((data[b:b+8].rstrip(b'\0').decode(),u32(b+12),u32(b+8),u32(b+20),u32(b+16)))
def r2o(r):
    for n,va,vs,ra,rs in secs:
        if va<=r<va+max(vs,rs): return r-va+ra
    return None
IMAGE_BASE=u32(opt+28)
imp=r2o(u32(opt+96+1*8))
slots={}
i=0
while True:
    b=imp+i*20
    oft=u32(b); nm=u32(b+12); ft=u32(b+16)
    if oft==0 and nm==0 and ft==0: break
    no=r2o(nm); dll=data[no:data.index(b'\0',no)].decode()
    to=r2o(oft if oft else ft); iatrva=ft
    j=0
    while True:
        v=u32(to+j*4)
        if v==0: break
        if v & 0x80000000: fn='#%d'%(v&0xffff)
        else:
            fo=r2o(v&0x7fffffff); fn=data[fo+2:data.index(b'\0',fo+2)].decode()
        slots[IMAGE_BASE+iatrva+j*4]=(dll,fn)
        j+=1
    i+=1
# scan .text for FF 15 <abs>
text=[s for s in secs if s[0]=='.text'][0]
_,tva,tvs,tra,trs=text
refs={}
buf=data[tra:tra+trs]
for k in range(len(buf)-6):
    if buf[k]==0xFF and buf[k+1]==0x15:
        a=struct.unpack_from('<I',buf,k+2)[0]
        if a in slots:
            refs.setdefault(slots[a],[]).append(IMAGE_BASE+tva+k)
targets=sys.argv[1:] if len(sys.argv)>1 else []
for (dll,fn),locs in sorted(refs.items()):
    if targets and not any(t.lower() in fn.lower() for t in targets): continue
    print("%-16s %-32s %d calls: %s"%(dll,fn,len(locs),' '.join('0x%X'%x for x in locs[:12])))
