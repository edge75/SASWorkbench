import json, io
s=json.load(io.open("inv.json",encoding="utf-8"))
p={r[0]:r for r in s["PollTemplates.xml"]}
r_=({r[0]:r for r in s["ResponseTemplates.xml"]})
e={r[0]:r for r in s["EventTemplates.xml"]}
o=io.open("tables.md","w",encoding="utf-8")
allc=sorted(set(p)|set(r_))
o.write("## Poll+Response combined (%d poll, %d resp, union %d)\n\n"%(len(p),len(r_),len(allc)))
o.write("| cmd | poll name | P crc | P len | P mg | P #direct | P #total | resp name | R crc | R len | R #direct | R #total |\n|---|---|---|---|---|---|---|---|---|---|---|---|\n")
for c in allc:
    a=p.get(c); b=r_.get(c)
    def f(x,i,d="-"):
        return str(x[i]) if x else d
    o.write("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n"%(
        c, f(a,1,"**(none)**"), f(a,2), f(a,3), f(a,4), f(a,5), f(a,6),
        f(b,1,"**(none)**"), f(b,2), f(b,3), f(b,5), f(b,6)))
o.write("\n## poll-only cmds (%d): %s\n"%(len(set(p)-set(r_)), ", ".join(sorted(set(p)-set(r_)))))
o.write("\n## response-only cmds (%d): %s\n"%(len(set(r_)-set(p)), ", ".join(sorted(set(r_)-set(p)))))
o.write("\n## Events (%d)\n\n| code | name | #direct | #total |\n|---|---|---|---|\n"%len(e))
for c in sorted(e):
    x=e[c]; o.write("| %s | %s | %s | %s |\n"%(c,x[1],x[5],x[6]))
o.close()
print("poll",len(p),"resp",len(r_),"ev",len(e))
print("poll-only",sorted(set(p)-set(r_)))
print("resp-only",sorted(set(r_)-set(p)))
