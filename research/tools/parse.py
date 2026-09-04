import xml.etree.ElementTree as ET, sys, io, collections, json, os
base = r"C:\igs\GSA SAS Host Simulator v1.1"
out = io.open(sys.argv[1], "w", encoding="utf-8")

def load(p):
    # strip DOCTYPE to avoid external DTD fetch
    raw = io.open(p, "r", encoding="latin-1").read()
    raw = raw.replace('<!DOCTYPE SAS_templates SYSTEM "SASTemplates.dtd">','')
    return ET.fromstring(raw)

def count_fields(el):
    n=0
    for c in el:
        n+=1
        n+=count_fields(c)
    return n

def direct_fields(el):
    return len(list(el))

files = ["PollTemplates.xml","ResponseTemplates.xml","EventTemplates.xml",
         "UserPollTemplates.xml","UserResponseTemplates.xml",
         r"Defaults\PollTemplates.xml", r"Defaults\ResponseTemplates.xml"]
summary={}
for f in files:
    root = load(os.path.join(base,f))
    tpls = root.findall("SAS_template")
    out.write("### %s : %d SAS_template elements, root tag=%s\n" % (f, len(tpls), root.tag))
    # tag histogram
    hist = collections.Counter()
    for t in root.iter():
        hist[t.tag]+=1
    out.write("tag histogram: %s\n" % json.dumps(dict(hist), indent=None))
    # attribute value domains
    attrs = collections.defaultdict(collections.Counter)
    for t in root.iter():
        for k,v in t.attrib.items():
            attrs[t.tag+"@"+k][v]+=1
    for k in sorted(attrs):
        vals=attrs[k]
        if k.endswith("@name") or k.endswith("@cmd"):
            out.write("  %s : %d distinct values\n" % (k, len(vals)))
        else:
            out.write("  %s : %s\n" % (k, json.dumps(dict(vals.most_common()))))
    out.write("\n")
    summary[f]=[(t.get("cmd"), t.get("name"), t.get("crc","no"), t.get("length","no"), t.get("multigame","no"), direct_fields(t), count_fields(t)) for t in tpls]

json.dump(summary, io.open(sys.argv[2],"w",encoding="utf-8"), indent=1)
out.close()
