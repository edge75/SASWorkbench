import io,re,json
h=io.open(r"C:\Progetti\libsas\include\libsas.h",encoding="latin-1").read()
m=re.search(r"enum SasExceptionCode \{(.*?)\n\};",h,re.S)
body=m.group(1)
enum={}
for name,val in re.findall(r"(\w+)\s*=\s*(0x[0-9A-Fa-f]+)",body):
    enum["%02X"%int(val,16)]=name
s=json.load(io.open("inv.json",encoding="utf-8"))
gsa={ (r[0] if len(r[0])==2 else "0"+r[0]):r[1] for r in s["EventTemplates.xml"]}
o=io.open("events.md","w",encoding="utf-8")
o.write("libsas exception codes: %d ; GSA event templates: %d\n\n"%(len(enum),len(gsa)))
o.write("GSA only (%d): %s\n\n"%(len(set(gsa)-set(enum)), ", ".join("%s=%s"%(c,gsa[c]) for c in sorted(set(gsa)-set(enum)))))
o.write("libsas only (%d): %s\n\n"%(len(set(enum)-set(gsa)), ", ".join("%s=%s"%(c,enum[c]) for c in sorted(set(enum)-set(gsa)))))
o.write("both: %d\n"%len(set(gsa)&set(enum)))
o.close()
print(open("events.md",encoding="utf-8").read()[:4000])
