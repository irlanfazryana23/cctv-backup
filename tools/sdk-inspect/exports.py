import struct, sys, re
f = r'C:\Program Files (x86)\CMS\NetSdk.dll'
b = open(f, 'rb').read()
pe = struct.unpack_from('<I', b, 0x3C)[0]
nsec = struct.unpack_from('<H', b, pe + 6)[0]
optsz = struct.unpack_from('<H', b, pe + 20)[0]
opt = pe + 24
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
want = sys.argv[1:]
res = {}
for i in range(nn):
    nr = struct.unpack_from('<I', b, no + 4 * i)[0]
    p = r2o(nr)
    e = b.index(b'\0', p)
    name = b[p:e].decode()
    ordv = struct.unpack_from('<H', b, oo + 2 * i)[0]
    frva = struct.unpack_from('<I', b, fo + 4 * ordv)[0]
    res[name] = frva

for w in want:
    if w not in res:
        print(w, 'NOT FOUND'); continue
    off = r2o(res[w])
    code = b[off:off + 0x600]
    # find forwarding jmp (thunk)
    first = code[:8].hex()
    rets = [(m.start(), struct.unpack_from('<H', code, m.start() + 1)[0]) for m in re.finditer(b'\xC2', code[:0x600]) if m.start() + 3 <= len(code)]
    ebp = sorted(set(code[i + 1] for i in range(len(code) - 1) if code[i] in (0x45, 0x4D, 0x55, 0x5D, 0x65, 0x6D, 0x75, 0x7D) and 0x08 <= code[i + 1] < 0x80 and code[i + 1] % 4 == 0))
    print(w, hex(res[w]), first, 'ret imm16 candidates:', [hex(v) for _, v in rets[:6]], 'ebp+ offsets:', [hex(x) for x in ebp][-6:])
