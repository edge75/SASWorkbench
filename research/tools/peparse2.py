import struct, sys, re, datetime

import os
PATH = os.environ.get("PE_PATH", r"C:\IGT3\sastest.exe")
data = open(PATH, 'rb').read()

def u8(o): return data[o]
def u16(o): return struct.unpack_from('<H', data, o)[0]
def u32(o): return struct.unpack_from('<I', data, o)[0]

e_lfanew = u32(0x3C)
assert data[e_lfanew:e_lfanew+4] == b'PE\0\0', "not PE"
coff = e_lfanew + 4
machine = u16(coff); nsec = u16(coff+2); timestamp = u32(coff+4)
sizeopt = u16(coff+16); chars = u16(coff+18)
opt = coff + 20
magic = u16(opt)
sect_tbl = opt + sizeopt

out = []
def p(s=''): out.append(s)

p("=== PE HEADER ===")
p("File: %s  size=%d" % (PATH, len(data)))
p("Machine=0x%04x  NumberOfSections=%d  TimeDateStamp=0x%08x" % (machine, nsec, timestamp))
p("  Link date (UTC): %s" % datetime.datetime.utcfromtimestamp(timestamp).isoformat())
p("OptionalHeader magic=0x%04x (%s) size=%d" % (magic, 'PE32' if magic == 0x10b else 'PE32+', sizeopt))
p("Characteristics=0x%04x" % chars)
p("LinkerVersion=%d.%d" % (u8(opt+2), u8(opt+3)))
p("SizeOfCode=0x%x  AddressOfEntryPoint=0x%x  ImageBase=0x%x" % (u32(opt+4), u32(opt+16), u32(opt+28)))
p("Subsystem=%d (2=GUI,3=CUI)  OSVer=%d.%d  SubsysVer=%d.%d" % (u16(opt+68), u16(opt+40), u16(opt+42), u16(opt+48), u16(opt+50)))
nrva = u32(opt+92)
DIRNAMES = ['Export','Import','Resource','Exception','Certificate','BaseReloc','Debug','Arch',
            'GlobalPtr','TLS','LoadConfig','BoundImport','IAT','DelayImport','CLR','Reserved']
dirs = []
for i in range(nrva):
    rva = u32(opt+96+i*8); sz = u32(opt+96+i*8+4)
    dirs.append((rva, sz))
    if rva:
        p("  Dir[%d] %s: RVA=0x%08x Size=0x%x" % (i, DIRNAMES[i] if i < 16 else '?', rva, sz))

p("")
p("=== SECTIONS ===")
sections = []
for i in range(nsec):
    b = sect_tbl + i*40
    name = data[b:b+8].rstrip(b'\0').decode('ascii', 'replace')
    vsize = u32(b+8); vaddr = u32(b+12); rsize = u32(b+16); raddr = u32(b+20); c = u32(b+36)
    sections.append((name, vaddr, vsize, raddr, rsize, c))
    p("  %-10s VA=0x%08x VSize=0x%08x RawPtr=0x%08x RawSize=0x%08x Char=0x%08x" % (name, vaddr, vsize, raddr, rsize, c))

def rva2off(rva):
    for name, va, vs, ra, rs, c in sections:
        if va <= rva < va + max(vs, rs):
            off = rva - va + ra
            if off < len(data):
                return off
    return None

RT = {1:'RT_CURSOR',2:'RT_BITMAP',3:'RT_ICON',4:'RT_MENU',5:'RT_DIALOG',6:'RT_STRING',
      7:'RT_FONTDIR',8:'RT_FONT',9:'RT_ACCELERATOR',10:'RT_RCDATA',11:'RT_MESSAGETABLE',
      12:'RT_GROUP_CURSOR',14:'RT_GROUP_ICON',16:'RT_VERSION',17:'RT_DLGINCLUDE',
      19:'RT_PLUGPLAY',20:'RT_VXD',21:'RT_ANICURSOR',22:'RT_ANIICON',23:'RT_HTML',24:'RT_MANIFEST'}

res_rva, res_size = dirs[2] if len(dirs) > 2 else (0, 0)
res_base = rva2off(res_rva) if res_rva else None
resources = []

def res_str(off):
    ln = u16(res_base+off)
    return data[res_base+off+2: res_base+off+2+ln*2].decode('utf-16le', 'replace')

def walk(off, level, path):
    base = res_base + off
    nnamed = u16(base+12); nid = u16(base+14)
    for i in range(nnamed+nid):
        e = base+16+i*8
        nameval = u32(e); offval = u32(e+4)
        nm = res_str(nameval & 0x7fffffff) if (nameval & 0x80000000) else nameval
        if offval & 0x80000000:
            walk(offval & 0x7fffffff, level+1, path+[nm])
        else:
            do = res_base + offval
            resources.append((path+[nm], u32(do), u32(do+4), u32(do+8)))

if res_base is not None:
    walk(0, 0, [])

def resdata(drva, dsz):
    o = rva2off(drva)
    return data[o:o+dsz]

p("")
p("=== RESOURCE INVENTORY (%d leaves) ===" % len(resources))
bytype = {}
for path, drva, dsz, dcp in resources:
    bytype.setdefault(path[0], []).append((path, drva, dsz, dcp))
for t in sorted(bytype, key=lambda x: (isinstance(x, str), x)):
    tn = RT.get(t, str(t)) if isinstance(t, int) else '"%s"' % t
    ids = [pp[1] for pp, _, _, _ in bytype[t]]
    p("  Type %s: %d entries" % (tn, len(bytype[t])))
    p("      names/ids = %s" % (ids,))

# ---------------- MENU ----------------
MF = {0x0001:'GRAYED',0x0002:'DISABLED',0x0008:'CHECKED',0x0010:'POPUP',
      0x0020:'MENUBARBREAK',0x0040:'MENUBREAK',0x0100:'HILITE',0x0800:'OWNERDRAW'}

def parse_menu(buf):
    lines = []
    ver = struct.unpack_from('<H', buf, 0)[0]
    hdr = struct.unpack_from('<H', buf, 2)[0]
    if ver != 0:
        lines.append(('MENUEX version=%d (not decoded)' % ver, None))
        return lines
    start = 4 + hdr
    def rec(o, depth):
        while o < len(buf):
            opt = struct.unpack_from('<H', buf, o)[0]; o += 2
            mid = None
            if not (opt & 0x0010):
                mid = struct.unpack_from('<H', buf, o)[0]; o += 2
            s = o
            while o+1 < len(buf) and struct.unpack_from('<H', buf, o)[0] != 0:
                o += 2
            cap = buf[s:o].decode('utf-16le', 'replace'); o += 2
            flags = [v for k, v in MF.items() if (opt & k) and k != 0x0010]
            fs = (' [' + ','.join(flags) + ']') if flags else ''
            if opt & 0x0010:
                lines.append(('  '*depth + '+ ' + (cap if cap else '(unnamed popup)') + fs, None))
                o = rec(o, depth+1)
            else:
                disp = cap if cap else ('---- separator ----' if mid == 0 else '(blank)')
                lines.append(('  '*depth + '- ' + disp + fs, mid))
            if opt & 0x0080:
                return o
        return o
    rec(start, 0)
    return lines

p("")
p("")
p("=== RT_MENU (decoded menu trees) ===")
for path, drva, dsz, dcp in bytype.get(4, []):
    buf = resdata(drva, dsz)
    p("")
    p("--- MENU id/name = %s   lang=%s   bytes=%d ---" % (path[1], path[2], dsz))
    for txt, mid in parse_menu(buf):
        if mid is None:
            p("  %s" % txt)
        else:
            p("  %-62s ID=%d" % (txt, mid))

# ---------------- DIALOG ----------------
CLS = {0x80:'BUTTON',0x81:'EDIT',0x82:'STATIC',0x83:'LISTBOX',0x84:'SCROLLBAR',0x85:'COMBOBOX'}
BSMAP = {0:'BS_PUSHBUTTON',1:'BS_DEFPUSHBUTTON',2:'BS_CHECKBOX',3:'BS_AUTOCHECKBOX',
         4:'BS_RADIOBUTTON',5:'BS_3STATE',6:'BS_AUTO3STATE',7:'BS_GROUPBOX',8:'BS_USERBUTTON',
         9:'BS_AUTORADIOBUTTON',10:'BS_PUSHBOX',11:'BS_OWNERDRAW'}
SSMAP = {0:'SS_LEFT',1:'SS_CENTER',2:'SS_RIGHT',3:'SS_ICON',4:'SS_BLACKRECT',5:'SS_GRAYRECT',
         6:'SS_WHITERECT',7:'SS_BLACKFRAME',8:'SS_GRAYFRAME',9:'SS_WHITEFRAME',11:'SS_SIMPLE',
         12:'SS_LEFTNOWORDWRAP',13:'SS_OWNERDRAW',14:'SS_BITMAP',16:'SS_ENHMETAFILE',
         17:'SS_ETCHEDHORZ',18:'SS_ETCHEDVERT',19:'SS_ETCHEDFRAME'}
DLGSTY = [(0x40,'DS_SETFONT'),(0x4,'DS_3DLOOK'),(0x8,'DS_FIXEDSYS'),(0x800,'DS_MODALFRAME'),
          (0x2000,'DS_CENTER'),(0x1,'DS_ABSALIGN'),(0x80,'DS_NOIDLEMSG'),
          (0x100,'DS_SETFOREGROUND'),(0x400,'DS_CONTROL')]
WSMAP = [(0x80000000,'WS_POPUP'),(0x40000000,'WS_CHILD'),(0x20000000,'WS_MINIMIZE'),
         (0x10000000,'WS_VISIBLE'),(0x08000000,'WS_DISABLED'),(0x04000000,'WS_CLIPSIBLINGS'),
         (0x02000000,'WS_CLIPCHILDREN'),(0x01000000,'WS_MAXIMIZE'),(0x00C00000,'WS_CAPTION'),
         (0x00080000,'WS_SYSMENU'),(0x00040000,'WS_THICKFRAME'),(0x00020000,'WS_GROUP'),
         (0x00010000,'WS_TABSTOP'),(0x00200000,'WS_VSCROLL'),(0x00100000,'WS_HSCROLL'),
         (0x00800000,'WS_BORDER')]

class Rdr:
    def __init__(self, b): self.b = b; self.o = 0
    def w(self):
        v = struct.unpack_from('<H', self.b, self.o)[0]; self.o += 2; return v
    def d(self):
        v = struct.unpack_from('<I', self.b, self.o)[0]; self.o += 4; return v
    def sz(self):
        s = self.o
        while self.o+1 < len(self.b) and struct.unpack_from('<H', self.b, self.o)[0] != 0:
            self.o += 2
        v = self.b[s:self.o].decode('utf-16le', 'replace'); self.o += 2; return v
    def name_or_ord(self):
        if self.o+2 > len(self.b): return None
        v = struct.unpack_from('<H', self.b, self.o)[0]
        if v == 0:
            self.o += 2; return None
        if v == 0xFFFF:
            self.o += 2; return ('ord', self.w())
        return ('str', self.sz())
    def align(self):
        if self.o % 4:
            self.o += 4 - (self.o % 4)

def decode_style(style, cls):
    names = [n for m, n in WSMAP if (style & m) == m and m]
    if cls == 'BUTTON':
        names.append(BSMAP.get(style & 0xF, 'BS_0x%x' % (style & 0xF)))
    elif cls == 'STATIC':
        names.append(SSMAP.get(style & 0x1F, 'SS_0x%x' % (style & 0x1f)))
    elif cls == 'EDIT':
        for m, n in [(0x0080,'ES_AUTOHSCROLL'),(0x0040,'ES_AUTOVSCROLL'),(0x0004,'ES_MULTILINE'),
                     (0x0020,'ES_PASSWORD'),(0x0800,'ES_READONLY'),(0x2000,'ES_NUMBER'),
                     (0x0100,'ES_NOHIDESEL'),(0x0001,'ES_CENTER'),(0x0002,'ES_RIGHT'),
                     (0x1000,'ES_WANTRETURN'),(0x0008,'ES_UPPERCASE')]:
            if style & m: names.append(n)
    elif cls == 'LISTBOX':
        for m, n in [(0x0001,'LBS_NOTIFY'),(0x0002,'LBS_SORT'),(0x0010,'LBS_OWNERDRAWFIXED'),
                     (0x0020,'LBS_OWNERDRAWVARIABLE'),(0x0040,'LBS_HASSTRINGS'),
                     (0x0100,'LBS_MULTIPLESEL'),(0x0200,'LBS_NOINTEGRALHEIGHT'),
                     (0x0800,'LBS_EXTENDEDSEL'),(0x1000,'LBS_DISABLENOSCROLL'),
                     (0x0008,'LBS_MULTICOLUMN'),(0x0004,'LBS_NOREDRAW'),(0x8000,'LBS_NODATA'),
                     (0x0400,'LBS_USETABSTOPS'),(0x0080,'LBS_WANTKEYBOARDINPUT')]:
            if style & m: names.append(n)
    elif cls == 'COMBOBOX':
        names.append({1:'CBS_SIMPLE',2:'CBS_DROPDOWN',3:'CBS_DROPDOWNLIST'}.get(style & 3, ''))
        for m, n in [(0x0100,'CBS_SORT'),(0x0200,'CBS_NOINTEGRALHEIGHT'),(0x0040,'CBS_AUTOHSCROLL'),
                     (0x0010,'CBS_OWNERDRAWFIXED'),(0x0020,'CBS_OWNERDRAWVARIABLE'),
                     (0x0002,'CBS_HASSTRINGS')]:
            if style & m: names.append(n)
    return ','.join([x for x in names if x])

def s16(v): return v - 65536 if v > 32767 else v

dialogs = []
p("")
p("")
p("=== RT_DIALOG (decoded) ===")
dlg_entries = bytype.get(5, [])
def sortkey(x):
    n = x[0][1]
    return (0, n, '') if isinstance(n, int) else (1, 0, str(n))
for path, drva, dsz, dcp in sorted(dlg_entries, key=sortkey):
    buf = resdata(drva, dsz)
    r = Rdr(buf)
    first = struct.unpack_from('<H', buf, 0)[0]
    second = struct.unpack_from('<H', buf, 2)[0]
    ex = (first == 0x0001 and second == 0xFFFF)
    if ex:
        r.o = 0; r.w(); r.w(); helpid = r.d(); exstyle = r.d(); style = r.d()
        cdit = r.w(); x = r.w(); y = r.w(); cx = r.w(); cy = r.w()
    else:
        style = r.d(); exstyle = r.d(); cdit = r.w()
        x = r.w(); y = r.w(); cx = r.w(); cy = r.w()
    menu = r.name_or_ord(); wcls = r.name_or_ord(); title = r.name_or_ord()
    font = None
    if style & 0x40:
        pt = r.w()
        if ex:
            r.w(); r.w()
        font = (pt, r.sz())
    tstr = title[1] if (title and title[0] == 'str') else (str(title) if title else '')
    ds = [n for m, n in DLGSTY if style & m] + [n for m, n in WSMAP if (style & m) == m and m]
    p("")
    p("--- DIALOG %s (lang %s) %s ---" % (path[1], path[2], 'DLGTEMPLATEEX' if ex else 'DLGTEMPLATE'))
    p('  Caption : "%s"' % tstr)
    p("  Rect    : x=%d y=%d cx=%d cy=%d  (dialog units)" % (s16(x), s16(y), cx, cy))
    p("  Style   : 0x%08x [%s]  ExStyle=0x%08x" % (style, ','.join(ds), exstyle))
    if font: p("  Font    : %s %dpt" % (font[1], font[0]))
    if menu: p("  Menu    : %s" % (menu,))
    if wcls: p("  Class   : %s" % (wcls,))
    p("  Controls: %d" % cdit)
    p("  %6s %-20s %-42s %5s %5s %5s %5s  STYLES" % ('ID','CLASS','CAPTION','X','Y','CX','CY'))
    ctrls = []
    for i in range(cdit):
        r.align()
        if r.o >= len(buf): break
        if ex:
            r.d(); cex = r.d(); cst = r.d()
            cx_ = r.w(); cy_ = r.w(); ccx = r.w(); ccy = r.w(); cid = r.d()
        else:
            cst = r.d(); cex = r.d()
            cx_ = r.w(); cy_ = r.w(); ccx = r.w(); ccy = r.w(); cid = r.w()
        cx_, cy_, ccx, ccy = s16(cx_), s16(cy_), s16(ccx), s16(ccy)
        cl = r.name_or_ord()
        if cl and cl[0] == 'ord':
            clsname = CLS.get(cl[1], 'ORD_0x%x' % cl[1])
        elif cl:
            clsname = cl[1]
        else:
            clsname = '(none)'
        tt = r.name_or_ord()
        if tt and tt[0] == 'str':
            cap = tt[1]
        elif tt and tt[0] == 'ord':
            cap = '#ordinal %d' % tt[1]
        else:
            cap = ''
        extra = r.w()
        if extra:
            r.o += extra
        st = decode_style(cst, clsname)
        capd = cap.replace('\n', '\\n').replace('\t', '\\t')
        capq = '"%s"' % capd if capd else ''
        p("  %6d %-20s %-42s %5d %5d %5d %5d  0x%08x %s" % (cid, clsname, capq, cx_, cy_, ccx, ccy, cst, st))
        ctrls.append(dict(id=cid, cls=clsname, cap=cap, x=cx_, y=cy_, cx=ccx, cy=ccy, style=cst, styles=st))
    dialogs.append(dict(id=path[1], title=tstr, cx=cx, cy=cy, controls=ctrls))

# ---------------- STRING TABLE ----------------
p("")
p("")
p("=== RT_STRING (string table) ===")
allstrings = {}
for path, drva, dsz, dcp in bytype.get(6, []):
    buf = resdata(drva, dsz)
    blk = path[1]
    if not isinstance(blk, int):
        continue
    o = 0
    for i in range(16):
        if o+2 > len(buf): break
        ln = struct.unpack_from('<H', buf, o)[0]; o += 2
        if ln:
            s = buf[o:o+ln*2].decode('utf-16le', 'replace'); o += ln*2
            allstrings[(blk-1)*16+i] = s
for sid in sorted(allstrings):
    p("  %6d: %r" % (sid, allstrings[sid]))

# ---------------- ACCELERATOR ----------------
p("")
p("")
p("=== RT_ACCELERATOR ===")
FVIRT = {0x01:'FVIRTKEY',0x02:'FNOINVERT',0x04:'FSHIFT',0x08:'FCONTROL',0x10:'FALT'}
VK = {0x08:'VK_BACK',0x09:'VK_TAB',0x0D:'VK_RETURN',0x1B:'VK_ESCAPE',0x20:'VK_SPACE',
      0x21:'VK_PRIOR',0x22:'VK_NEXT',0x23:'VK_END',0x24:'VK_HOME',0x25:'VK_LEFT',0x26:'VK_UP',
      0x27:'VK_RIGHT',0x28:'VK_DOWN',0x2D:'VK_INSERT',0x2E:'VK_DELETE',0x70:'VK_F1',0x71:'VK_F2',
      0x72:'VK_F3',0x73:'VK_F4',0x74:'VK_F5',0x75:'VK_F6',0x76:'VK_F7',0x77:'VK_F8',0x78:'VK_F9',
      0x79:'VK_F10',0x7A:'VK_F11',0x7B:'VK_F12'}
for path, drva, dsz, dcp in bytype.get(9, []):
    buf = resdata(drva, dsz)
    p("--- ACCEL table %s ---" % (path[1],))
    o = 0
    while o+8 <= len(buf):
        fv, key, cmd, pad = struct.unpack_from('<HHHH', buf, o); o += 8
        mods = [v for k, v in FVIRT.items() if fv & k]
        kn = VK.get(key, chr(key) if 32 <= key < 127 else '0x%x' % key)
        p("   %-30s key=%-10s cmd=%d" % ('+'.join(mods), kn, cmd))
        if fv & 0x80: break

# ---------------- VERSION ----------------
p("")
p("")
p("=== RT_VERSION ===")
for path, drva, dsz, dcp in bytype.get(16, []):
    buf = resdata(drva, dsz)
    p("--- VERSION %s size=%d ---" % (path[1], dsz))
    idx = buf.find(b'\xbd\x04\xef\xfe')
    if idx >= 0:
        fv = struct.unpack_from('<HHHH', buf, idx+8)
        pv = struct.unpack_from('<HHHH', buf, idx+16)
        p("  FileVersion    = %d.%d.%d.%d" % (fv[1], fv[0], fv[3], fv[2]))
        p("  ProductVersion = %d.%d.%d.%d" % (pv[1], pv[0], pv[3], pv[2]))
        p("  FileFlagsMask=0x%x FileFlags=0x%x FileOS=0x%x FileType=0x%x FileSubtype=0x%x" % (
            struct.unpack_from('<I', buf, idx+24)[0], struct.unpack_from('<I', buf, idx+28)[0],
            struct.unpack_from('<I', buf, idx+32)[0], struct.unpack_from('<I', buf, idx+36)[0],
            struct.unpack_from('<I', buf, idx+40)[0]))
    txt = buf.decode('utf-16le', 'replace')
    runs = re.findall(r'[\x20-\x7e]{2,}', txt)
    p("  UTF-16 string runs:")
    for rn in runs:
        p("    %r" % rn)

# ---------------- OTHER RESOURCE TYPES ----------------
p("")
p("")
p("=== RT_RCDATA / other named resources (first 400 bytes each) ===")
for t in bytype:
    if t in (1,2,3,4,5,6,9,12,14,16):
        continue
    for path, drva, dsz, dcp in bytype[t]:
        tn = RT.get(t, str(t)) if isinstance(t, int) else '"%s"' % t
        buf = resdata(drva, dsz)
        p("--- %s / %s (lang %s) size=%d ---" % (tn, path[1], path[2], dsz))
        try:
            asciirun = re.findall(rb'[\x20-\x7e]{4,}', buf[:4000])
            p("   ascii: " + ' | '.join(x.decode('ascii') for x in asciirun[:80]))
        except Exception as e:
            p("   (err %s)" % e)

# ---------------- IMPORTS ----------------
p("")
p("")
p("=== IMPORTS ===")
imp_rva, imp_sz = dirs[1]
impdesc = rva2off(imp_rva) if imp_rva else None
impout = []
if impdesc:
    i = 0
    while True:
        b = impdesc + i*20
        oft = u32(b); nm = u32(b+12); ft = u32(b+16)
        if oft == 0 and nm == 0 and ft == 0:
            break
        no = rva2off(nm)
        dll = data[no:data.index(b'\0', no)].decode('ascii', 'replace')
        thunk = oft if oft else ft
        to = rva2off(thunk)
        funcs = []
        j = 0
        while to is not None:
            v = u32(to + j*4)
            if v == 0: break
            if v & 0x80000000:
                funcs.append('#ordinal %d' % (v & 0xffff))
            else:
                fo = rva2off(v & 0x7fffffff)
                nmo = fo + 2
                funcs.append(data[nmo:data.index(b'\0', nmo)].decode('ascii', 'replace'))
            j += 1
        impout.append((dll, funcs))
        i += 1
p("Total DLLs: %d   Total imports: %d" % (len(impout), sum(len(f) for _, f in impout)))
for dll, funcs in impout:
    p("")
    p("--- %s  (%d imports) ---" % (dll, len(funcs)))
    for k in range(0, len(funcs), 4):
        p("   " + "".join("%-30s" % f for f in funcs[k:k+4]))

p("")
p("")
p("=== EXPORTS ===")
if dirs[0][0]:
    p("  Export directory RVA 0x%x size %d" % dirs[0])
else:
    p("  (none)")

open(sys.argv[1], 'w', encoding='utf-8').write('\n'.join(out))
print("[written] lines=%d" % len(out))
