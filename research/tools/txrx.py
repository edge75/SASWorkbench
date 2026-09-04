import re, collections, sys
TS = re.compile(r'^[A-Z][a-z]{2} [A-Z][a-z]{2} \d\d \d\d:\d\d:\d\d \d{4}')
for path in (r'C:\IGT3\TXRXData.dat', r'C:\Progetti\libsas\utils\igt\TXRXData.dat'):
    c=collections.Counter(); tx=collections.Counter(); rx=collections.Counter()
    n=0; first=None; last=None; other=[]
    with open(path,'rb') as f:
        for line in f:
            n+=1
            s=line.decode('latin1').rstrip('\r\n')
            if s.startswith('TX>='): c['TX']+=1; tx[s[4:].strip()]+=1
            elif s.startswith('RX<='): c['RX']+=1; rx[s[4:].strip()]+=1
            elif TS.match(s):
                c['TIMESTAMP']+=1
                if first is None: first=s
                last=s
            elif s.strip()=='': c['blank']+=1
            else:
                c['other']+=1
                if len(other)<15: other.append(s)
    print('='*70)
    print(path)
    print('  total lines: %d' % n)
    for k,v in c.most_common(): print('    %-10s %d' % (k,v))
    print('  first timestamp: %s' % first)
    print('  last  timestamp: %s' % last)
    print('  distinct TX payloads: %d' % len(tx))
    for k,v in tx.most_common(20): print('      TX>= %-40r x%d' % (k,v))
    print('  distinct RX payloads: %d' % len(rx))
    for k,v in rx.most_common(20): print('      RX<= %-40r x%d' % (k,v))
    if other:
        print('  sample "other" lines:')
        for o in other: print('      %r' % o)
    print()
