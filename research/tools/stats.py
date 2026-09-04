import xml.etree.ElementTree as ET, io, os, collections
base=r"C:\igs\GSA SAS Host Simulator v1.1"
for f in ["PollTemplates.xml","ResponseTemplates.xml","EventTemplates.xml"]:
    raw=io.open(os.path.join(base,f),encoding="latin-1").read().replace('<!DOCTYPE SAS_templates SYSTEM "SASTemplates.dtd">','')
    r=ET.fromstring(raw)
    maxd=0; f1=0; namedopt=0; anonopt=0
    def rec(e,d,od):
        global maxd,f1,namedopt,anonopt
        nd = od+1 if e.tag=="optional" else od
        maxd=max(maxd,nd)
        if e.tag=="optional":
            if e.get("name"): namedopt+=1
            else: anonopt+=1
        if e.tag in ("choice_entry","option_choice_entry") and e.get("value")=="F1": f1+=1
        for c in e: rec(c,d+1,nd)
    for t in r.findall("SAS_template"): rec(t,0,0)
    print(f, "max optional nesting:",maxd, "| named optionals:",namedopt,"| anonymous optionals:",anonopt,"| F1 'Unsupported value' entries:",f1)
