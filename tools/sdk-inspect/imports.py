"""Lists which DLLs a vendor DLL needs (recursively) and which of them live in the CMS folder.

usage: python imports.py NetSdk.dll [cms folder]
Anything not found in the CMS folder is assumed to be a Windows system DLL.
"""
import os
import struct
import sys


def imports_of(path):
    b = open(path, 'rb').read()
    pe = struct.unpack_from('<I', b, 0x3C)[0]
    nsec = struct.unpack_from('<H', b, pe + 6)[0]
    optsz = struct.unpack_from('<H', b, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', b, opt)[0]
    dd = opt + (112 if magic == 0x20B else 96)
    imp_rva = struct.unpack_from('<I', b, dd + 8)[0]          # import directory is entry 1
    delay_rva = struct.unpack_from('<I', b, dd + 13 * 8)[0]   # delay-load import is entry 13
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

    names = []
    if imp_rva:
        o = r2o(imp_rva)
        while True:
            name_rva = struct.unpack_from('<I', b, o + 12)[0]
            if name_rva == 0:
                break
            p = r2o(name_rva)
            names.append(b[p:b.index(b'\0', p)].decode())
            o += 20
    if delay_rva:
        o = r2o(delay_rva)
        while True:
            name_rva = struct.unpack_from('<I', b, o + 4)[0]
            if name_rva == 0:
                break
            p = r2o(name_rva)
            names.append(b[p:b.index(b'\0', p)].decode())
            o += 32
    return names


def main():
    root = sys.argv[2] if len(sys.argv) > 2 else r'C:\Program Files (x86)\CMS'
    start = sys.argv[1]
    local = {f.lower(): f for f in os.listdir(root)}
    seen, order, system = set(), [], set()
    todo = [start]
    while todo:
        name = todo.pop()
        if name.lower() in seen:
            continue
        seen.add(name.lower())
        if name.lower() in local:
            order.append(local[name.lower()])
            todo.extend(imports_of(os.path.join(root, local[name.lower()])))
        else:
            system.add(name)
    print('FROM CMS FOLDER:')
    for n in sorted(order):
        print('  ', n)
    print('SYSTEM / NOT IN CMS FOLDER:')
    for n in sorted(system):
        print('  ', n)


main()
