import io,re,json
# enum map
h=io.open(r"C:\Progetti\libsas\include\libsas.h",encoding="latin-1").read()
m=re.search(r"enum SasLongPollCode \{(.*?)\};",h,re.S)
enum={}
for name,val in re.findall(r"(LP_\w+)\s*=\s*(0x[0-9A-Fa-f]+)",m.group(1)):
    enum[name]=int(val,16)
t=io.open(r"C:\Progetti\libsas\src\libsas_threads.cpp",encoding="latin-1").read().split("\n")
# find the dispatcher switch region
start=None
for i,l in enumerate(t):
    if "case LP_SHUTDOWN:" in l and "0x01" in l:
        start=i; break
codes={}
i=start
depth=0
while i < len(t):
    l=t[i]
    mm=re.match(r"\s*case (LP_\w+):",l)
    if mm and mm.group(1) in enum:
        codes[enum[mm.group(1)]]=mm.group(1)
    if re.match(r"\s*\}\s*//\s*end.*switch",l) or ("default:" in l and i>start+50):
        break
    i+=1
    if i>start+900: break
print("dispatcher region lines",start,"->",i)
print("count",len(codes))
print(sorted("%02X"%c for c in codes))
io.open("libsas_codes.json","w").write(json.dumps({"%02X"%c:n for c,n in sorted(codes.items())},indent=1))
# also from lp.h function names
lph=io.open(r"C:\Progetti\libsas\include\lp.h",encoding="latin-1").read()
fn=sorted(set(re.findall(r"\blp([0-9A-F]{2})_(\w+)",lph)))
print("lp.h per-code functions:",len(fn))
print([a for a,b in fn])
