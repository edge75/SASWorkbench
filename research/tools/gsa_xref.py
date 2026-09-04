import struct,sys
PATH='C:'+chr(92)+'igs'+chr(92)+'GSA SAS Host Simulator v1.1'+chr(92)+'SASHostSim.exe'
data=open(PATH,'rb').read()
u16=lambda o: struct.unpack_from('<H',data,o)[0]
u32=lambda o: struct.unpack_from('<I',data,o)[0]
e=u32(0x3C);coff=e+4;nsec=u16(coff+2);so=u16(coff+16);opt=coff+20;st=opt+so
secs=[]
for i in range(nsec):
    b=st+i*40
    secs.append((data[b:b+8].rstrip(b'\0').decode(),u32(b+12),u32(b+8),u32(b+20),u32(b+16)))
IB=u32(opt+28)
t=[s for s in secs if s[0]=='.text'][0]
_,tva,tvs,tra,trs=t
target=int(sys.argv[1],16)
buf=data[tra:tra+trs]
for k in range(len(buf)-5):
    if buf[k]==0xE8:
        rel=struct.unpack_from('<i',buf,k+1)[0]
        va=IB+tva+k
        if va+5+rel==target: print('call from 0x%08X'%va)
    if buf[k]==0x68:  # push imm32
        v=struct.unpack_from('<I',buf,k+1)[0]
        if v==target: print('push imm 0x%08X at 0x%08X'%(v,IB+tva+k))
