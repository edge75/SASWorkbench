import glob, os
out=[]
for p in glob.glob(r"C:\igs\GSA SAS Host Simulator v1.1\DOSBox\*.txt")+glob.glob(r"C:\igs\GSA SAS Host Simulator v1.1\DOSBox\*.bat")+glob.glob(r"C:\igs\GSA SAS Host Simulator v1.1\*.bat"):
    d=open(p,'rb').read()
    s=None
    for enc in ('utf-8-sig','utf-8','cp950','cp1252'):
        try: s=d.decode(enc); enc_used=enc; break
        except Exception: pass
    out.append("="*70); out.append("FILE: %s  (%d bytes, %s)"%(os.path.basename(p),len(d),enc_used)); out.append(s or "")
open('dosbox-notes.txt','w',encoding='utf-8').write("\n".join(out))
print("wrote", len("\n".join(out)))
