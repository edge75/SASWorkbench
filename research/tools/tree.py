import re, sys
src = open(sys.argv[1], encoding='utf-8').read().splitlines()
KEEP = ('Caption', 'Text', 'Hint', 'Items.Strings', 'Lines.Strings', 'ShortCut',
        'OnClick', 'OnChange', 'Checked', 'Enabled', 'Visible', 'Name', 'Title',
        'Filter', 'DefaultExt', 'Interval', 'OnTimer', 'Max', 'Min', 'Position',
        'Associate', 'ReadOnly', 'TabVisible', 'Style', 'MaxLength', 'ColCount')
out = []
depth = 0
cur = None
buf = []
pending_items = None
for i, ln in enumerate(src):
    s = ln.strip()
    m = re.match(r'^object (\S*): (\w+)', s)
    if m:
        ind = (len(ln) - len(ln.lstrip())) // 2
        cur = (ind, m.group(1), m.group(2))
        out.append(('OBJ', ind, m.group(1), m.group(2), []))
        continue
    if s == 'end' or s.startswith('RCDATA') or s.startswith('==='):
        if s.startswith('RCDATA'):
            out.append(('HDR', 0, s, '', []))
        continue
    m2 = re.match(r"^(\w[\w.]*) = (.*)$", s)
    if m2 and out and out[-1][0] == 'OBJ':
        k, v = m2.group(1), m2.group(2)
        if k in KEEP or k.endswith('.Strings') or k.startswith('On'):
            out[-1][4].append((k, v))
    elif s.startswith('item[') and out and out[-1][0] == 'OBJ':
        out[-1][4].append(('   item', s))
    elif s.startswith('ascii:') and out and out[-1][0] == 'OBJ':
        pass

res = []
for e in out:
    if e[0] == 'HDR':
        res.append('')
        res.append('#' * 90)
        res.append('# ' + e[2])
        res.append('#' * 90)
        continue
    _, ind, name, cls, props = e
    pad = '  ' * ind
    cap = ''
    for k, v in props:
        if k == 'Caption' or k == 'Text':
            cap = '   ' + k + '=' + v
            break
    res.append('%s%s : %s%s' % (pad, name if name else '(unnamed)', cls, cap))
    for k, v in props:
        if k in ('Caption', 'Text'):
            continue
        vv = v
        if len(vv) > 400:
            vv = vv[:400] + ' ...'
        res.append('%s      . %s = %s' % (pad, k, vv))
print('\n'.join(res))
