import struct, re
d=open(r'C:\IGT3\sastest.exe','rb').read()
BS=bytes([0x5c])   # backslash
def find_all(pat):
    out=[];i=0
    while True:
        i=d.find(pat,i)
        if i<0:break
        out.append(i);i+=1
    return out
print("=== DOS-device prefix search ===")
pref = BS+BS+b'.'+BS          # \.\
print("  literal '(bs)(bs).(bs)' count:", len(find_all(pref)), [hex(x) for x in find_all(pref)][:10])
print("  '.(bs)COM' count:", len(find_all(b'.'+BS+b'COM')))
print("  '(bs)(bs)' count:", len(find_all(BS+BS)), [hex(x) for x in find_all(BS+BS)][:10])
print()
print("=== every 'COM<digits>' byte sequence in file ===")
for m in re.finditer(rb'COM[0-9]{1,2}', d):
    print('   off=0x%06x %-6s ctx=%r' % (m.start(), m.group().decode(), d[max(0,m.start()-10):m.start()+16]))
print("=== 'COM%' format strings ===", [hex(x) for x in find_all(b'COM%')])
print()
delta=0x400c00
def refs(off):
    return [hex(x) for x in find_all(struct.pack('<I', off+delta))]
print('pointer-array base file 0x%x -> VA 0x%x  refs=%s' % (0xb75fc, 0xb75fc+delta, refs(0xb75fc)))
for name,off in [('DCB_SPACE_1',0xb764c),('DCB_MARK',0xb766e),('DCB_SPACE_2',0xb7690),('errmsg',0xb76b2)]:
    print('%-12s VA=0x%08x refs=%s' % (name, off+delta, refs(off)))
for i,off in enumerate([0xb761c,0xb7622,0xb7628,0xb762e,0xb763a,0xb7640,0xb7646]):
    print('  COMstr[%d] "%s" VA=0x%08x refs=%s' % (i, d[off:d.index(b'\0',off)].decode(), off+delta, refs(off)))
