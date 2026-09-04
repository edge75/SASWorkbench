"""Decode Borland Delphi binary DFM form resources (TPF0) from sastest.exe RT_RCDATA."""
import struct, sys, re

PATH = r"C:\IGT3\sastest.exe"
data = open(PATH, 'rb').read()

def u16(o): return struct.unpack_from('<H', data, o)[0]
def u32(o): return struct.unpack_from('<I', data, o)[0]

e_lfanew = u32(0x3C)
coff = e_lfanew + 4
nsec = u16(coff+2); sizeopt = u16(coff+16)
opt = coff + 20
sect_tbl = opt + sizeopt
sections = []
for i in range(nsec):
    b = sect_tbl + i*40
    sections.append((data[b:b+8].rstrip(b'\0').decode(), u32(b+12), u32(b+8), u32(b+20), u32(b+16)))

def rva2off(rva):
    for name, va, vs, ra, rs in sections:
        if va <= rva < va + max(vs, rs):
            return rva - va + ra
    return None

res_rva = u32(opt+96+2*8)
res_base = rva2off(res_rva)
resources = []

def res_str(off):
    ln = u16(res_base+off)
    return data[res_base+off+2: res_base+off+2+ln*2].decode('utf-16le', 'replace')

def walk(off, path):
    base = res_base + off
    n = u16(base+12) + u16(base+14)
    for i in range(n):
        e = base+16+i*8
        nv = u32(e); ov = u32(e+4)
        nm = res_str(nv & 0x7fffffff) if (nv & 0x80000000) else nv
        if ov & 0x80000000:
            walk(ov & 0x7fffffff, path+[nm])
        else:
            do = res_base + ov
            resources.append((path+[nm], u32(do), u32(do+4)))
walk(0, [])

# ---------------- DFM reader ----------------
VA = {0:'vaNull',1:'vaList',2:'vaInt8',3:'vaInt16',4:'vaInt32',5:'vaExtended',6:'vaString',
      7:'vaIdent',8:'vaFalse',9:'vaTrue',10:'vaBinary',11:'vaSet',12:'vaLString',13:'vaNil',
      14:'vaCollection',15:'vaSingle',16:'vaCurrency',17:'vaDate',18:'vaWString',19:'vaInt64',
      20:'vaUTF8String'}

class DFM:
    def __init__(self, buf):
        self.b = buf; self.o = 0
        self.out = []
    def byte(self):
        v = self.b[self.o]; self.o += 1; return v
    def i16(self):
        v = struct.unpack_from('<h', self.b, self.o)[0]; self.o += 2; return v
    def i32(self):
        v = struct.unpack_from('<i', self.b, self.o)[0]; self.o += 4; return v
    def sstr(self):
        n = self.byte()
        v = self.b[self.o:self.o+n]; self.o += n
        return v.decode('latin-1')
    def lstr(self):
        n = self.i32()
        v = self.b[self.o:self.o+n]; self.o += n
        return v.decode('latin-1')
    def wstr(self):
        n = self.i32()
        v = self.b[self.o:self.o+n*2]; self.o += n*2
        return v.decode('utf-16le', 'replace')

    def value(self):
        t = self.byte()
        if t == 0: return None, 'vaNull'
        if t == 1:
            items = []
            while self.b[self.o] != 0:
                items.append(self.value()[0])
            self.o += 1
            return items, 'vaList'
        if t == 2: return self.byte(), 'vaInt8'
        if t == 3: return self.i16(), 'vaInt16'
        if t == 4: return self.i32(), 'vaInt32'
        if t == 5:
            raw = self.b[self.o:self.o+10]; self.o += 10
            return '<extended %s>' % raw.hex(), 'vaExtended'
        if t == 6: return self.sstr(), 'vaString'
        if t == 7: return self.sstr(), 'vaIdent'
        if t == 8: return False, 'vaFalse'
        if t == 9: return True, 'vaTrue'
        if t == 10:
            n = self.i32(); raw = self.b[self.o:self.o+n]; self.o += n
            return raw, 'vaBinary'
        if t == 11:
            s = []
            while True:
                e = self.sstr()
                if e == '': break
                s.append(e)
            return s, 'vaSet'
        if t == 12: return self.lstr(), 'vaLString'
        if t == 13: return None, 'vaNil'
        if t == 14:
            items = []
            while self.b[self.o] != 0:
                nb = self.b[self.o]
                if nb in (2, 3, 4):
                    self.value()
                props = {}
                while True:
                    nm = self.sstr()
                    if nm == '': break
                    props[nm] = self.value()[0]
                self.o += 1
                items.append(props)
            self.o += 1
            return items, 'vaCollection'
        if t == 15:
            v = struct.unpack_from('<f', self.b, self.o)[0]; self.o += 4; return v, 'vaSingle'
        if t == 16:
            v = struct.unpack_from('<q', self.b, self.o)[0]; self.o += 8; return v/10000.0, 'vaCurrency'
        if t == 17:
            v = struct.unpack_from('<d', self.b, self.o)[0]; self.o += 8; return v, 'vaDate'
        if t == 18: return self.wstr(), 'vaWString'
        if t == 19:
            v = struct.unpack_from('<q', self.b, self.o)[0]; self.o += 8; return v, 'vaInt64'
        if t == 20:
            n = self.i32(); v = self.b[self.o:self.o+n].decode('utf-8', 'replace'); self.o += n
            return v, 'vaUTF8String'
        raise ValueError('unknown value type %d at 0x%x' % (t, self.o-1))

    def obj(self, depth):
        pfx = self.b[self.o]
        kind = ''
        if (pfx & 0xF0) == 0xF0:
            self.o += 1
            fl = pfx & 0x0F
            if fl & 2:  # ffChildPos
                self.value()
            kind = ' {flags=%d}' % fl
        cls = self.sstr()
        name = self.sstr()
        ind = '  ' * depth
        self.out.append('%sobject %s: %s%s' % (ind, name, cls, kind))
        while True:
            pn = self.sstr()
            if pn == '': break
            v, vt = self.value()
            self.emit_prop(ind + '  ', pn, v, vt)
        while self.b[self.o] != 0:
            self.obj(depth+1)
        self.o += 1
        self.out.append('%send' % ind)

    def emit_prop(self, ind, pn, v, vt):
        if vt == 'vaBinary':
            self.out.append('%s%s = <BINARY %d bytes>' % (ind, pn, len(v)))
            txt = re.findall(rb'[\x20-\x7e]{4,}', v[:2000])
            if txt:
                self.out.append('%s   ascii: %s' % (ind, b' | '.join(txt[:30]).decode('latin-1')))
        elif vt == 'vaList':
            flat = []
            for x in v:
                flat.append(repr(x) if not isinstance(x, bytes) else '<bin %d>' % len(x))
            self.out.append('%s%s = (%s)' % (ind, pn, ', '.join(flat)))
        elif vt == 'vaCollection':
            self.out.append('%s%s = < %d items >' % (ind, pn, len(v)))
            for i, it in enumerate(v):
                pretty = ', '.join('%s=%r' % (k, (('<bin %d>' % len(vv)) if isinstance(vv, bytes) else vv))
                                   for k, vv in it.items())
                self.out.append('%s   item[%d] %s' % (ind, i, pretty))
        elif vt == 'vaSet':
            self.out.append('%s%s = [%s]' % (ind, pn, ', '.join(v)))
        elif vt in ('vaString', 'vaLString', 'vaWString', 'vaUTF8String'):
            self.out.append('%s%s = %r' % (ind, pn, v))
        elif vt == 'vaIdent':
            self.out.append('%s%s = %s' % (ind, pn, v))
        else:
            self.out.append('%s%s = %r' % (ind, pn, v))


targets = [(p, r, s) for p, r, s in resources if p[0] == 10]
res_out = []
for path, drva, dsz in targets:
    off = rva2off(drva)
    buf = data[off:off+dsz]
    name = path[1]
    res_out.append('')
    res_out.append('=' * 100)
    res_out.append('RCDATA "%s"  size=%d  sig=%r' % (name, dsz, buf[:4]))
    res_out.append('=' * 100)
    if buf[:4] != b'TPF0':
        res_out.append('  (not a DFM; raw ascii): %s' % buf[:400])
        continue
    d = DFM(buf)
    d.o = 4
    try:
        d.obj(0)
    except Exception as e:
        d.out.append('*** PARSE ERROR at 0x%x: %s' % (d.o, e))
    res_out.extend(d.out)

txt = '\n'.join(res_out)
open(sys.argv[1], 'w', encoding='utf-8').write(txt)
print('[written] %s  lines=%d' % (sys.argv[1], len(res_out)))
