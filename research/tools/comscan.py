import re
BS = chr(92).encode()
d=open('C:'+chr(92)+'igs'+chr(92)+'GSA SAS Host Simulator v1.1'+chr(92)+'SASHostSim.exe','rb').read()
pat = BS+BS+b'.'+BS
print("--- literal UNC device prefix occurrences ---", pat)
for m in re.finditer(re.escape(pat), d):
    print(hex(m.start()), d[m.start():m.start()+24])
print("--- 'COM' occurrences with context ---")
for m in re.finditer(rb'COM', d):
    s=max(0,m.start()-48); print(hex(m.start()), d[s:m.start()+16])
