import json,io
s=json.load(io.open("inv.json",encoding="utf-8"))
gsa={r[0]:r[1] for r in s["PollTemplates.xml"]}
gsar={r[0]:r[1] for r in s["ResponseTemplates.xml"]}
lib=json.load(io.open("libsas_codes.json"))
o=io.open("coverage.md","w",encoding="utf-8")
both=sorted(set(gsa)&set(lib)); gonly=sorted(set(gsa)-set(lib)); lonly=sorted(set(lib)-set(gsa))
o.write("GSA poll cmds: %d ; libsas dispatched: %d ; both: %d ; GSA-only: %d ; libsas-only: %d\n\n"%(len(gsa),len(lib),len(both),len(gonly),len(lonly)))
o.write("### In GSA PollTemplates but NOT dispatched by libsas (%d)\n\n| cmd | GSA name |\n|---|---|\n"%len(gonly))
for c in gonly: o.write("| %s | %s |\n"%(c,gsa[c]))
o.write("\n### Dispatched by libsas but NOT in GSA PollTemplates (%d)\n\n| cmd | libsas symbol |\n|---|---|\n"%len(lonly))
for c in lonly: o.write("| %s | %s |\n"%(c,lib[c]))
o.write("\n### In both (%d): %s\n"%(len(both), ", ".join(both)))
# also GSA response coverage vs libsas
o.write("\n### GSA has a poll template but NO response template (%d): %s\n"%(len(set(gsa)-set(gsar)), ", ".join(sorted(set(gsa)-set(gsar)))))
o.close()
print(open("coverage.md",encoding="utf-8").read())
