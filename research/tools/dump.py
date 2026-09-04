import xml.etree.ElementTree as ET, sys, io, os
base = r"C:\igs\GSA SAS Host Simulator v1.1"
raw = io.open(os.path.join(base,sys.argv[1]), "r", encoding="latin-1").read()
raw = raw.replace('<!DOCTYPE SAS_templates SYSTEM "SASTemplates.dtd">','')
root = ET.fromstring(raw)
out=io.open(sys.argv[2],"w",encoding="utf-8")
def rec(el,d):
    a=el.attrib
    if el.tag in ("choice_entry","option_choice_entry") and len(el)==0:
        return
    s="  "*d+el.tag+" "+" ".join("%s=%r"%(k,a[k]) for k in ("name","cmd","size","offset","length","radix","crc","multigame","value") if k in a)
    if el.tag in ("choice_parm",):
        ents=el.findall("choice_entry")
        s+="  [%d entries: %s]"%(len(ents), ", ".join("%s=%s"%(e.get("value"),e.get("name")) for e in ents[:5]))
    out.write(s+"\n")
    for c in el:
        rec(c,d+1)
for t in root.findall("SAS_template"):
    rec(t,0)
out.close()
