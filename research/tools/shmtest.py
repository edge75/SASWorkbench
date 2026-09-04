import mmap, struct, os, shutil
P = r"C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm"
print("size:", os.path.getsize(P))
orig = open(P,"rb").read()
shutil.copy(P, r"C:\Users\alessio.brizi.QUIXANT\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\shm_backup.bin")
f = open(P, "r+b")
m = mmap.mmap(f.fileno(), 1024)          # third process, full RW view
hb, intr, dio = struct.unpack_from("<III", m, 0)
lamps = struct.unpack_from("<32I", m, 12)
bna, prn = struct.unpack_from("<II", m, 140)
print("BEFORE heartbeat=%d intrusion=0x%08X dio=0x%08X bna=0x%08X printer=0x%08X" % (hb,intr,dio,bna,prn))
print("lamps:", lamps)
# write test: set intrusion bit 2 (Main) and printer bit 2 (Paper Out)
struct.pack_into("<I", m, 4, intr | (1<<2))
struct.pack_into("<I", m, 144, prn | (1<<2))
m.flush()
i2, = struct.unpack_from("<I", m, 4); p2, = struct.unpack_from("<I", m, 144)
print("AFTER  intrusion=0x%08X printer=0x%08X  -> WRITE OK: %s" % (i2,p2, (i2==intr|(1<<2) and p2==prn|(1<<2))))
# restore
m.seek(0); m.write(orig); m.flush()
chk = bytes(m[:1024])
print("RESTORED identical:", chk == orig)
m.close(); f.close()
print("file identical on disk:", open(P,"rb").read() == orig)
