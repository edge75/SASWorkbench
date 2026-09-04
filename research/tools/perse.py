import struct, sys, io

PATH = r"C:\IGT3\sastest.exe"
data = open(PATH,'rb').read()
out = []
def P(*a):
    s = " ".join(str(x) for x in a)
    out.append(s)
    print(s)

e_lfanew = struct.unpack_from('<I', data, 0x3C)[0]
assert data[e_lfanew:e_lfanew+4] == b'PE\0\0', data[e_lfanew:e_lfanew+4]
coff = e_lfanew+4
machine, nsec, timedate, ptrsym, nsym, optsize, chars = struct.unpack_from('<HHIIIHH', data, coff)
P("=== PE HEADER ===")
P("Machine: 0x%04x  NumberOfSections: %d  TimeDateStamp: %d (0x%x)  OptHdrSize: %d  Characteristics: 0x%04x" % (machine,nsec,timedate,timedate,optsize,chars))
import datetime
P("Link date (UTC):", datetime.datetime.utcfromtimestamp(timedate).isoformat())
opt = coff+20
magic = struct.unpack_from('<H', data, opt)[0]
P("Optional magic: 0x%04x (%s)" % (magic, 'PE32' if magic==0x10b else 'PE32+'))
(lmaj,lmin) = struct.unpack_from('<BB', data, opt+2)
sizecode, sizeinit, sizeuninit, epRVA, baseCode = struct.unpack_from('<IIIII', data, opt+4)
baseData, imgBase = struct.unpack_from('<II', data, opt+24)
sectAlign, fileAlign = struct.unpack_from('<II', data, opt+32)
subsystem = struct.unpack_from('<H', data, opt+68)[0]
numdirs = struct.unpack_from('<I', data, opt+92)[0]
P("Linker %d.%d  EntryPoint RVA 0x%x  ImageBase 0x%x  Subsystem %d (%s)  NumDataDirs %d" % (
   lmaj,lmin,epRVA,imgBase,subsystem, {2:'WINDOWS_GUI',3:'WINDOWS_CUI'}.get(subsystem,'?'), numdirs))
dirs = []
dd = opt+96
for i in range(numdirs):
    rva,sz = struct.unpack_from('<II', data, dd+i*8)
    dirs.append((rva,sz))
DIRNAMES=['EXPORT','IMPORT','RESOURCE','EXCEPTION','SECURITY','BASERELOC','DEBUG','ARCH','GLOBALPTR','TLS','LOADCFG','BOUNDIMPORT','IAT','DELAYIMPORT','COMDESC','RSVD']
for i,(r,s) in enumerate(dirs):
    if r or s:
        P("  DataDir[%2d] %-12s RVA 0x%08x size %d" % (i, DIRNAMES[i] if i<len(DIRNAMES) else '?', r, s))

sects=[]
st = opt+optsize
P("=== SECTIONS ===")
for i in range(nsec):
    o=st+i*40
    name = data[o:o+8].rstrip(b'\0').decode('latin1')
    vsize,vaddr,rawsize,rawptr = struct.unpack_from('<IIII', data, o+8)
    schars = struct.unpack_from('<I', data, o+36)[0]
    sects.append((name,vaddr,vsize,rawptr,rawsize))
    P("  %-8s VA 0x%08x VSize 0x%08x RawPtr 0x%08x RawSize 0x%08x Chars 0x%08x" % (name,vaddr,vsize,rawptr,rawsize,schars))

def rva2off(rva):
    for name,va,vs,rp,rs in sects:
        if va <= rva < va+max(vs,rs):
            return rp + (rva-va)
    return None

# ---------------- IMPORTS ----------------
P("")
P("=== IMPORTS ===")
imprva, impsz = dirs[1]
if imprva:
    o = rva2off(imprva)
    idx=0
    while True:
        ent = data[o+idx*20:o+idx*20+20]
        if len(ent)<20: break
        oft, tds, fc, nameRVA, ft = struct.unpack('<IIIII', ent)
        if oft==0 and nameRVA==0 and ft==0: break
        no = rva2off(nameRVA)
        dllname = data[no:data.index(b'\0', no)].decode('latin1')
        P("--- %s ---" % dllname)
        thunk = oft if oft else ft
        to = rva2off(thunk)
        j=0
        funcs=[]
        while True:
            v = struct.unpack_from('<I', data, to+j*4)[0]
            if v==0: break
            if v & 0x80000000:
                funcs.append("Ordinal#%d" % (v & 0xFFFF))
            else:
                ho = rva2off(v)
                nm = data[ho+2:data.index(b'\0', ho+2)].decode('latin1')
                funcs.append(nm)
            j+=1
        for k in range(0,len(funcs),4):
            P("    " + "  ".join("%-28s"%f for f in funcs[k:k+4]))
        P("    (%d functions)" % len(funcs))
        idx+=1

open(r"C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research\pe-header-imports.txt","w",encoding="utf-8").write("\n".join(out))
