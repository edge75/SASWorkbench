import re
d = open(r'C:\IGT3\sastest.exe','rb').read()
print("file size", len(d))
pat = b'\\.\'          # backslash backslash dot backslash
print("=== occurrences of literal \\.\ ===")
n=0
for m in re.finditer(re.escape(pat), d):
    n+=1
    print(hex(m.start()), d[m.start():m.start()+40])
print("count:", n)
print()
print("=== ALL 'COM' tokens (ascii) ===")
for m in re.finditer(rb'COM[0-9A-Za-z%:_]{0,14}', d):
    print(hex(m.start()), m.group())
print()
print("=== 'COM' tokens (utf-16le) ===")
for m in re.finditer(rb'C\x00O\x00M\x00(?:[\x20-\x7e]\x00){0,10}', d):
    print(hex(m.start()), m.group().decode('utf-16le'))
