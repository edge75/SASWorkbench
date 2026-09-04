# -*- coding: utf-8 -*-
import struct, sys, os, re
PATH = r"C:\IGT3\sastest.exe"
OUT  = r"C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research\exe-resources.txt"
data = open(PATH,'rb').read()
lines=[]
def P(s=""):
    lines.append(s)

e_lfanew = struct.unpack_from('<I', data, 0x3C)[0]
coff = e_lfanew+4
nsec = struct.unpack_from('<H', data, coff+2)[0]
optsize = struct.unpack_from('<H', data, coff+16)[0]
opt = coff+20
dirs=[]
for i in range(16):
    dirs.append(struct.unpack_from('<II', data, opt+96+i*8))
sects=[]
st=opt+optsize
for i in range(nsec):
    o=st+i*40
    name=data[o:o+8].rstrip(b'\0').decode('latin1')
    vsize,vaddr,rawsize,rawptr=struct.unpack_from('<IIII',data,o+8)
    sects.append((name,vaddr,vsize,rawptr,rawsize))
def rva2off(rva):
    for name,va,vs,rp,rs in sects:
        if va <= rva < va+max(vs,rs): return rp+(rva-va)
    return None

RSRC_RVA, RSRC_SZ = dirs[2]
RSRC_OFF = rva2off(RSRC_RVA)

TYPES={1:'RT_CURSOR',2:'RT_BITMAP',3:'RT_ICON',4:'RT_MENU',5:'RT_DIALOG',6:'RT_STRING',
7:'RT_FONTDIR',8:'RT_FONT',9:'RT_ACCELERATOR',10:'RT_RCDATA',11:'RT_MESSAGETABLE',
12:'RT_GROUP_CURSOR',14:'RT_GROUP_ICON',16:'RT_VERSION',17:'RT_DLGINCLUDE',
19:'RT_PLUGPLAY',20:'RT_VXD',21:'RT_ANICURSOR',22:'RT_ANIICON',23:'RT_HTML',24:'RT_MANIFEST'}

def read_name(off):
    ln = struct.unpack_from('<H', data, RSRC_OFF+off)[0]
    return data[RSRC_OFF+off+2:RSRC_OFF+off+2+ln*2].decode('utf-16-le', 'replace')

resources=[]
def walk(diroff, level, path):
    base = RSRC_OFF+diroff
    nnamed, nid = struct.unpack_from('<HH', data, base+12)
    for i in range(nnamed+nid):
        eo = base+16+i*8
        nameval, offval = struct.unpack_from('<II', data, eo)
        if nameval & 0x80000000:
            nm = read_name(nameval & 0x7FFFFFFF)
        else:
            nm = nameval
        if offval & 0x80000000:
            walk(offval & 0x7FFFFFFF, level+1, path+[nm])
        else:
            drva, dsize, cp, rsv = struct.unpack_from('<IIII', data, RSRC_OFF+offval)
            resources.append((path+[nm], rva2off(drva), dsize))
walk(0,0,[])

P("="*78)
P("RESOURCE INVENTORY  -  C:\\IGT3\\sastest.exe")
P("="*78)
counts={}
for path,off,size in resources:
    t=path[0]
    tn = TYPES.get(t, str(t)) if isinstance(t,int) else str(t)
    counts[tn]=counts.get(tn,0)+1
for k,v in sorted(counts.items()):
    P("  %-18s %d" % (k,v))
P("")

def wstr(off):
    s=[]
    while True:
        c=struct.unpack_from('<H', data, off)[0]
        off+=2
        if c==0: break
        s.append(chr(c))
    return "".join(s), off

P("="*78); P("RT_MENU  (type 4)"); P("="*78)
MF={0x0001:'GRAYED',0x0002:'DISABLED',0x0008:'CHECKED',0x0020:'MENUBARBREAK',
    0x0040:'MENUBREAK',0x0100:'HELP'}
menu_ids={}
for path,off,size in sorted(resources, key=lambda r:(str(r[0][0]),str(r[0][1]))):
    if path[0]!=4: continue
    P("")
    P("--- MENU %s (lang %s) size=%d ---" % (path[1], path[2], size))
    ver, hdrsz = struct.unpack_from('<HH', data, off)
    p = off+4+hdrsz
    end = off+size
    depth=0
    stack=[]
    while p < end-1:
        opt = struct.unpack_from('<H', data, p)[0]; p+=2
        isend = bool(opt & 0x80)
        if opt & 0x10:
            txt,p = wstr(p)
            flags=[MF[b] for b in MF if (opt & b)]
            P("  "*(depth+1) + "+ %s%s" % (txt.replace('&',''), (" ["+",".join(flags)+"]") if flags else ""))
            stack.append(isend)
            depth+=1
            continue
        else:
            mid = struct.unpack_from('<H', data, p)[0]; p+=2
            txt,p = wstr(p)
            if txt=='' and mid==0:
                P("  "*(depth+1)+"  ----------------------------------------")
            else:
                flags=[MF[b] for b in MF if (opt & b)]
                P("  "*(depth+1)+"  %-46s  id=%d (0x%04x)%s" % (txt.replace('&',''), mid, mid, (" ["+",".join(flags)+"]") if flags else ""))
                if mid not in menu_ids: menu_ids[mid]=txt.replace('&','')
        if isend:
            while stack:
                popped = stack.pop()
                depth-=1
                if not popped:
                    break
            if depth<0: depth=0

P("")
P("="*78); P("RT_DIALOG  (type 5)"); P("="*78)
CLASSORD={0x80:'BUTTON',0x81:'EDIT',0x82:'STATIC',0x83:'LISTBOX',0x84:'SCROLLBAR',0x85:'COMBOBOX'}
BS={0:'PUSHBUTTON',1:'DEFPUSHBUTTON',2:'CHECKBOX',3:'AUTOCHECKBOX',4:'RADIOBUTTON',
    5:'3STATE',6:'AUTO3STATE',7:'GROUPBOX',8:'USERBUTTON',9:'AUTORADIOBUTTON',
    10:'PUSHBOX',11:'OWNERDRAW'}
def szOrOrd(p):
    v=struct.unpack_from('<H', data, p)[0]
    if v==0x0000:
        return ('', p+2)
    if v==0xFFFF:
        o=struct.unpack_from('<H', data, p+2)[0]
        return ('#ORD%d'%o, p+4)
    s,np = wstr(p)
    return (s, np)

dlgs=[]
for path,off,size in resources:
    if path[0]!=5: continue
    dlgs.append((path,off,size))
def sortkey(r):
    n=r[0][1]
    return (0,n,'') if isinstance(n,int) else (1,0,str(n))
dlgs.sort(key=sortkey)
for path,off,size in dlgs:
    p=off
    sig1,sig2 = struct.unpack_from('<HH', data, p)
    ex = (sig1==0xFFFF and sig2==0x0001)
    P("")
    P("-"*78)
    if ex:
        p+=4
        helpid, exstyle, style = struct.unpack_from('<III', data, p); p+=12
        cdit, x,y,cx,cy = struct.unpack_from('<Hhhhh', data, p); p+=10
    else:
        style, exstyle = struct.unpack_from('<II', data, p); p+=8
        cdit, x,y,cx,cy = struct.unpack_from('<Hhhhh', data, p); p+=10
    menu, p = szOrOrd(p)
    cls, p = szOrOrd(p)
    title, p = wstr(p)
    fontname=''; fontsize=0
    if style & 0x40:
        fontsize = struct.unpack_from('<H', data, p)[0]; p+=2
        if ex: p+=4
        fontname, p = wstr(p)
    P('DIALOG id=%s (lang %s)%s' % (path[1], path[2], '  [DLGTEMPLATEEX]' if ex else ''))
    P('  Caption : "%s"' % title)
    P('  Size    : x=%d y=%d w=%d h=%d (dlg units)   Style=0x%08X ExStyle=0x%08X  Controls=%d' % (x,y,cx,cy,style,exstyle,cdit))
    if fontname: P('  Font    : %s %d' % (fontname, fontsize))
    if menu: P('  Menu    : %s' % (menu,))
    if cls:  P('  Class   : %s' % (cls,))
    P('  %-4s %-20s %-44s %-8s %-6s %s' % ('#','CLASS','TEXT','ID','POS','STYLE'))
    try:
        for i in range(cdit):
            p = (p+3) & ~3
            if ex:
                chelp, cex, cstyle = struct.unpack_from('<III', data, p); p+=12
                cx_,cy_,ccx,ccy = struct.unpack_from('<hhhh', data, p); p+=8
                cid = struct.unpack_from('<I', data, p)[0]; p+=4
            else:
                cstyle, cex = struct.unpack_from('<II', data, p); p+=8
                cx_,cy_,ccx,ccy = struct.unpack_from('<hhhh', data, p); p+=8
                cid = struct.unpack_from('<H', data, p)[0]; p+=2
            v=struct.unpack_from('<H', data, p)[0]
            if v==0xFFFF:
                ordv = struct.unpack_from('<H', data, p+2)[0]; p+=4
                cname = CLASSORD.get(ordv, 'ORD0x%02X'%ordv)
            else:
                cname, p = wstr(p)
            ctext, p = szOrOrd(p)
            cbextra = struct.unpack_from('<H', data, p)[0]; p+=2
            p += cbextra
            detail=''
            if cname=='BUTTON':
                detail = BS.get(cstyle & 0x0F,'')
                if cstyle & 0x1000: detail += ',NOTIFY'
            elif cname=='STATIC':
                detail = {0:'LEFT',1:'CENTER',2:'RIGHT',3:'ICON',5:'BITMAP',6:'ETCHEDHORZ',7:'ETCHEDVERT',16:'BLACKFRAME'}.get(cstyle & 0x1F,'sty%d'%(cstyle&0x1F))
            elif cname=='EDIT':
                f=[]
                if cstyle & 0x0800: f.append('READONLY')
                if cstyle & 0x0004: f.append('MULTILINE')
                if cstyle & 0x0020: f.append('PASSWORD')
                if cstyle & 0x2000: f.append('NUMBER')
                if cstyle & 0x0100: f.append('WANTRETURN')
                if cstyle & 0x0200: f.append('VSCROLL_ok')
                detail=",".join(f)
            elif cname=='COMBOBOX':
                detail={1:'SIMPLE',2:'DROPDOWN',3:'DROPDOWNLIST'}.get(cstyle&3,'')
            sid = '%d'%cid if cid not in (0xFFFF,0xFFFFFFFF) else '-1'
            P('  %-4d %-20s %-44s %-8s %-6s 0x%08X %s' % (i+1, cname, '"%s"'%ctext if ctext else '', sid, '%d,%d %dx%d'%(cx_,cy_,ccx,ccy), cstyle, detail))
    except Exception as e:
        P('  !! parse error at control %d: %s' % (i, e))
P("")
P("Total dialogs: %d" % len(dlgs))

P("")
P("="*78); P("RT_STRING  (type 6)"); P("="*78)
strs={}
for path,off,size in resources:
    if path[0]!=6: continue
    block = path[1]
    p=off; end=off+size
    for i in range(16):
        if p+2>end: break
        ln = struct.unpack_from('<H', data, p)[0]; p+=2
        if ln:
            s = data[p:p+ln*2].decode('utf-16-le','replace'); p+=ln*2
            sid = (block-1)*16+i
            strs[sid]=s
for sid in sorted(strs):
    P("  %6d  %s" % (sid, strs[sid].replace('\n','\\n').replace('\r','\\r')))
P("  (total %d strings)" % len(strs))

P("")
P("="*78); P("RT_ACCELERATOR (type 9)"); P("="*78)
FVIRT={0x01:'VIRTKEY',0x02:'NOINVERT',0x04:'SHIFT',0x08:'CONTROL',0x10:'ALT'}
VK={0x08:'BACK',0x09:'TAB',0x0D:'RETURN',0x1B:'ESCAPE',0x20:'SPACE',0x2D:'INSERT',0x2E:'DELETE',
    0x24:'HOME',0x23:'END',0x21:'PGUP',0x22:'PGDN',0x25:'LEFT',0x26:'UP',0x27:'RIGHT',0x28:'DOWN',
    0x70:'F1',0x71:'F2',0x72:'F3',0x73:'F4',0x74:'F5',0x75:'F6',0x76:'F7',0x77:'F8',0x78:'F9',
    0x79:'F10',0x7A:'F11',0x7B:'F12'}
anyacc=False
for path,off,size in resources:
    if path[0]!=9: continue
    anyacc=True
    P("--- ACCEL TABLE %s ---" % (path[1],))
    p=off; end=off+size
    while p+8<=end:
        fv, key, cmd, pad = struct.unpack_from('<HHHH', data, p); p+=8
        mods=[FVIRT[b] for b in FVIRT if fv&b]
        kn = VK.get(key, chr(key) if 32<=key<127 else '0x%02X'%key)
        combo="+".join([m for m in mods if m not in('VIRTKEY','NOINVERT')]+[kn])
        P("   %-28s -> id=%d (0x%04x)  %s" % (combo, cmd, cmd, menu_ids.get(cmd,'')))
        if fv & 0x80: break
if not anyacc: P("  (none)")

P("")
P("="*78); P("RT_VERSION (type 16)"); P("="*78)
for path,off,size in resources:
    if path[0]!=16: continue
    blob = data[off:off+size]
    sig = blob.find(b'\xbd\x04\xef\xfe')
    if sig>=0:
        sv, fv_ms, fv_ls, pv_ms, pv_ls, fflagsmask, fflags, fos, ftype, fsubtype = struct.unpack_from('<IIIIIIIIII', blob, sig)
        P("  FileVersion   : %d.%d.%d.%d" % (fv_ms>>16, fv_ms&0xFFFF, fv_ls>>16, fv_ls&0xFFFF))
        P("  ProductVersion: %d.%d.%d.%d" % (pv_ms>>16, pv_ms&0xFFFF, pv_ls>>16, pv_ls&0xFFFF))
        P("  FileFlags 0x%X  FileOS 0x%X  FileType 0x%X" % (fflags,fos,ftype))
    txt = blob.decode('utf-16-le','ignore')
    toks=[t for t in re.split('\x00+', txt) if len(t)>=2 and all(31<ord(c)<127 for c in t)]
    P("  Raw VS_VERSION strings:")
    for t in toks:
        P("     %s" % t)

P("")
P("="*78); P("OTHER RESOURCE TYPES (names only)"); P("="*78)
for path,off,size in resources:
    t=path[0]
    if t in (4,5,6,9,16): continue
    tn = TYPES.get(t, str(t)) if isinstance(t,int) else str(t)
    P("  %-16s name=%-28s lang=%-5s size=%d" % (tn, path[1], path[2], size))

open(OUT,'w',encoding='utf-8').write("\n".join(lines))
print("WROTE %s  (%d lines)" % (OUT, len(lines)))
