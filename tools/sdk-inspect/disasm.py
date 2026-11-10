import struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

f = r'C:\Program Files (x86)\CMS\NetSdk.dll'
b = open(f, 'rb').read()
pe = struct.unpack_from('<I', b, 0x3C)[0]
nsec = struct.unpack_from('<H', b, pe + 6)[0]
optsz = struct.unpack_from('<H', b, pe + 20)[0]
opt = pe + 24
image_base = struct.unpack_from('<I', b, opt + 28)[0]
exp_rva = struct.unpack_from('<I', b, opt + 96)[0]
secs = []
sp = opt + optsz
for i in range(nsec):
    o = sp + 40 * i
    vs, va, rs, raw = struct.unpack_from('<IIII', b, o + 8)
    secs.append((va, max(vs, rs), raw))


def r2o(r):
    for va, sz, raw in secs:
        if va <= r < va + sz:
            return r - va + raw


eo = r2o(exp_rva)
nfun, nn = struct.unpack_from('<II', b, eo + 20)
funcs_rva, names_rva, ord_rva = struct.unpack_from('<III', b, eo + 28)
fo, no, oo = r2o(funcs_rva), r2o(names_rva), r2o(ord_rva)
res = {}
for i in range(nn):
    nr = struct.unpack_from('<I', b, no + 4 * i)[0]
    p = r2o(nr)
    e = b.index(b'\0', p)
    name = b[p:e].decode()
    ordv = struct.unpack_from('<H', b, oo + 2 * i)[0]
    res[name] = struct.unpack_from('<I', b, fo + 4 * ordv)[0]

md = Cs(CS_ARCH_X86, CS_MODE_32)
limit = int(sys.argv[1])
for w in sys.argv[2:]:
    # Accept either an export name or a raw RVA written as 0x... (e.g. 0x3648)
    rva = int(w, 16) if w.lower().startswith('0x') else res[w]
    off = r2o(rva)
    print('=====', w, hex(rva))
    n = 0
    for ins in md.disasm(b[off:off + 0x400], image_base + rva):
        print('  %x: %s %s' % (ins.address - image_base, ins.mnemonic, ins.op_str))
        n += 1
        if ins.mnemonic in ('ret',) or n >= limit:
            break
