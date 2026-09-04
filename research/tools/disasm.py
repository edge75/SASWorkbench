import sys, capstone
d=open(r'C:\IGT3\sastest.exe','rb').read()
start=int(sys.argv[1],16); end=int(sys.argv[2],16)
md=capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail=False
base = start + 0x400a00
for ins in md.disasm(d[start:end], base):
    print("0x%08x  %-24s %s %s" % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
