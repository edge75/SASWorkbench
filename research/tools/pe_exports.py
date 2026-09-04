import struct, sys
f=open(sys.argv[1],'rb'); d=f.read(); f.close()
pe=struct.unpack_from('<I',d,0x3c)[0]
assert d[pe:pe+4]==b'PE\0\0'
nsec=struct.unpack_from('<H',d,pe+6)[0]
optsz=struct.unpack_from('<H',d,pe+20)[0]
opt=pe+24
magic=struct.unpack_from('<H',d,opt)[0]
print("magic",hex(magic),"PE32+" if magic==0x20b else "PE32")
ddoff = opt + (112 if magic==0x20b else 96)
erva,esz=struct.unpack_from('<II',d,ddoff)
secs=[]
so=opt+optsz
for i in range(nsec):
    b=so+i*40
    name=d[b:b+8].rstrip(b'\0').decode()
    vs,va,rs,pr=struct.unpack_from('<IIII',d,b+8)
    secs.append((name,va,vs,rs,pr))
def r2o(rva):
    for name,va,vs,rs,pr in secs:
        if va<=rva<va+max(vs,rs): return pr+(rva-va)
    return None
eo=r2o(erva)
nfun,nnam=struct.unpack_from('<II',d,eo+20)
afun,anam,aord=struct.unpack_from('<III',d,eo+28)
no=r2o(anam)
names=[]
for i in range(nnam):
    nr=struct.unpack_from('<I',d,no+4*i)[0]
    o=r2o(nr); e=d.index(b'\0',o)
    names.append(d[o:e].decode())
print("export count:",nfun,"named:",nnam)
for n in sorted(names): print(n)
