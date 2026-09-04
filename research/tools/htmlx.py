import re, sys, html
src, dst = sys.argv[1], sys.argv[2]
t = open(src, encoding='utf-8', errors='replace').read()
t = re.sub(r'(?is)<script.*?</script>', ' ', t)
t = re.sub(r'(?is)<style.*?</style>', ' ', t)
# rows/cells -> separators
t = re.sub(r'(?i)</t[dh]>', ' \x01', t)
t = re.sub(r'(?i)</tr>', ' \x02', t)
t = re.sub(r'(?i)<br\s*/?>', ' \x01', t)
for tag in ('p','div','li','h1','h2','h3','h4','h5','table','section','header','footer'):
    t = re.sub(r'(?i)</%s>' % tag, ' \x02', t)
t = re.sub(r'(?s)<[^>]+>', ' ', t)
t = html.unescape(t)
t = t.replace('\x01', ' | ').replace('\x02', '\n')
lines = []
for ln in t.split('\n'):
    ln = re.sub(r'[ \t ]+', ' ', ln).strip()
    ln = re.sub(r'(\s*\|\s*)+$', '', ln).strip()
    if ln and ln != '|':
        lines.append(ln)
open(dst, 'w', encoding='utf-8').write('\n'.join(lines))
print("wrote", dst, len(lines), "lines")
