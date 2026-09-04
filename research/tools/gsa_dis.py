import struct, sys
from capstone import *
PATH='C:'+chr(92)+'igs'+chr(92)+'GSA SAS Host Simulator v1.1'+chr(92)+'SASHostSim.exe'
data=open(PATH,'rb').read()
u16=lambda o: struct.unpack_from('<H',data,o)[0]
u32=lambda o: struct.unpack_from('<I',data,o)[0]
e=u32(0x3C); coff=e+4; nsec=u16(coff+2); so=u16(coff+16); opt=coff+20; st=opt+so
secs=[]
for i in range(nsec):
    b=st+i*40
    secs.append((data[b:b+8].rstrip(b'\0').decode(),u32(b+12),u32(b+8),u32(b+20),u32(b+16)))
IB=u32(opt+28)
def va2o(va):
    r=va-IB
    for nm,va0,vs,ra,rs in secs:
        if va0<=r<va0+max(vs,rs): return r-va0+ra
    return None
def cstr(va):
    o=va2o(va)
    if o is None: return None
    end=data.find(b'\0',o)
    if end<0 or end-o>200: return None
    s=data[o:end]
    try: t=s.decode('ascii')
    except: return None
    return t
start=int(sys.argv[1],16); n=int(sys.argv[2]) if len(sys.argv)>2 else 40
back=int(sys.argv[3]) if len(sys.argv)>3 else 120
md=Cs(CS_ARCH_X86,CS_MODE_32)
o=va2o(start-back)
cnt=0
for i in md.disasm(data[o:o+back+n*10], start-back):
    ann=''
    for tok in i.op_str.replace(',',' ').split():
        t=tok.strip('[]')
        if t.startswith('0x'):
            try: v=int(t,16)
            except: continue
            s=cstr(v)
            if s and 2<=len(s)<120 and all(32<=ord(c)<127 for c in s): ann='   ; "%s"'%s
    print('0x%08x  %-8s %s%s'%(i.address, i.mnemonic, i.op_str, ann))
    cnt+=1
    if i.address>=start and cnt> (back//2): 
        pass
    if i.address>start+n*6: break
