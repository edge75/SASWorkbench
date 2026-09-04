d=open(r'C:\IGT3\sastest.exe','rb').read()
i=d.find(b'COM1:')
print('offset of "COM1:" = 0x%x' % i)
print(repr(d[i-160:i+340]))
print()
print('--- device-namespace / format-string probes ---')
BS = chr(92)
probes = {
 'backslash-backslash-dot-backslash': (BS+BS+'.'+BS).encode(),
 'UNC device prefix + COM'          : (BS+BS+'.'+BS+'COM').encode(),
 'COM%d'   : b'COM%d',
 'COM%s'   : b'COM%s',
 'COM%i'   : b'COM%i',
 'COM9:'   : b'COM9:',
 'COM10'   : b'COM10',
 '\\\\.\\ (2 bs)': b'\x5c\x5c.\x5c',
 'CreateFileA': b'CreateFileA',
}
for k,v in probes.items():
    idxs=[]
    s=0
    while True:
        j=d.find(v,s)
        if j<0: break
        idxs.append(hex(j)); s=j+1
        if len(idxs)>6: break
    print('%-34s count=%-3d %s' % (k, len(idxs), idxs))
print()
print('--- all "COM" occurrences with 24 bytes context ---')
s=0
n=0
while n<40:
    j=d.find(b'COM',s)
    if j<0: break
    ctx=d[j:j+10]
    if ctx[:4] in (b'COM1',b'COM2',b'COM3',b'COM4',b'COM5',b'COM6',b'COM7',b'COM8',b'COM9',b'COM0') or b'COM%' in ctx:
        print(hex(j), repr(d[j-8:j+24]))
        n+=1
    s=j+1
