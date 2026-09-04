import re
d=open(r'C:\IGT3\sastest.exe','rb').read()
for disp,label in [(0x79c,'WakeupDelay?'),(0x7a1,'MultiWakeup?'),(0x199,'WakeupEnabled')]:
    pat = disp.to_bytes(4,'little')
    hits=[m.start() for m in re.finditer(re.escape(pat), d[0x600:0xb0400])]
    print(f'disp 0x{disp:03x} ({label}): {len(hits)} raw hits -> VAs', [hex(h+0x600+0x400a00) for h in hits][:14])
