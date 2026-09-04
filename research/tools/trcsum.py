import os,re,glob
root = r"C:\igs\GSA SAS Host Simulator v1.1"
files = sorted(glob.glob(os.path.join(root,"10.*","10.*","*.trc"))) + sorted(glob.glob(os.path.join(root,"*.trc")))
out=[]
out.append("%-34s %8s %6s %6s %5s %5s %5s %5s  %-19s %s" % ("path","bytes","lines","evts","HOST","EGM","sep","pause","trace-started","span_s"))
tot=0
for f in files:
    b=open(f,'rb').read(); tot+=len(b)
    t=b.decode('cp1252','replace').replace('\r\n','\n')
    lines=t.split('\n')
    started=""
    m=re.match(r'Trace started at (.*)', lines[0] or "")
    if m: started=m.group(1).strip()
    host=sum(1 for l in lines if l.startswith('HOST '))
    egm=sum(1 for l in lines if l.startswith('EGM  '))
    sep=sum(1 for l in lines if l.startswith('----------'))
    pau=sum(1 for l in lines if l.startswith('***PAUSE'))
    ts=[float(x) for x in re.findall(r'^(?:HOST|EGM)\s+(\d+\.\d)\s', t, re.M)]
    span = sum(ts)/1000.0
    rel=os.path.relpath(f,root).replace(chr(92),'/')
    out.append("%-34s %8d %6d %6d %5d %5d %5d %5d  %-19s %.1f" % (rel,len(b),len(lines),host+egm,host,egm,sep,pau,started,span))
out.append("TOTAL BYTES = %d across %d files" % (tot,len(files)))
open('trc-summary.txt','w',encoding='utf-8').write('\n'.join(out))
print('\n'.join(out))
