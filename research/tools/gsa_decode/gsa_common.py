"""Shared primitives for the GSA SAS Host Simulator v1.1 on-disk formats.

Every binary file the simulator writes uses the same three primitives
(little-endian throughout, ANSI/latin-1 text):

    WORD    = uint16 LE
    DWORD   = uint32 LE
    CSTRING = BYTE length, then <length> bytes of latin-1 text
              (MFC CString-style, but always the 1-byte short form;
               no name in the shipped data exceeds 255 chars)

Files begin with `WORD fileVersion` and (except ComPortSettings.dat /
TestConfigFile.dat, which hold exactly one record) `DWORD recordCount`.
"""
import struct


class R:
    """Little-endian cursor over a bytes object."""

    def __init__(self, data, off=0):
        self.d = data
        self.o = off

    # --- scalars -------------------------------------------------------
    def u8(self):
        v = self.d[self.o]; self.o += 1; return v

    def i8(self):
        v = struct.unpack_from('<b', self.d, self.o)[0]; self.o += 1; return v

    def u16(self):
        v = struct.unpack_from('<H', self.d, self.o)[0]; self.o += 2; return v

    def i16(self):
        v = struct.unpack_from('<h', self.d, self.o)[0]; self.o += 2; return v

    def u32(self):
        v = struct.unpack_from('<I', self.d, self.o)[0]; self.o += 4; return v

    def i32(self):
        v = struct.unpack_from('<i', self.d, self.o)[0]; self.o += 4; return v

    def f64(self):
        v = struct.unpack_from('<d', self.d, self.o)[0]; self.o += 8; return v

    # --- strings / blobs ----------------------------------------------
    def cstr(self):
        n = self.u8()
        s = self.d[self.o:self.o + n].decode('latin-1')
        self.o += n
        return s

    def blob(self, n):
        b = self.d[self.o:self.o + n]; self.o += n; return b

    # --- helpers -------------------------------------------------------
    def eof(self):
        return self.o >= len(self.d)

    def left(self):
        return len(self.d) - self.o

    def peek(self, n=16):
        return self.d[self.o:self.o + n].hex(' ')


def load(path):
    with open(path, 'rb') as f:
        return f.read()


def hexdump(b, indent='    ', width=16):
    out = []
    for i in range(0, len(b), width):
        chunk = b[i:i + width]
        txt = ''.join(chr(c) if 32 <= c < 127 else '.' for c in chunk)
        out.append('%s%04X  %-*s  %s' % (indent, i, width * 3, chunk.hex(' '), txt))
    return '\n'.join(out)
