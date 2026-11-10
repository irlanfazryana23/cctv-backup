import struct, sys

path, names = sys.argv[1], set(sys.argv[2:])
d = open(path, 'rb').read()
bs, nblocks, dirsize, _, bmaddr = struct.unpack_from('<IIIII', d, 32)[0], 0, 0, 0, 0
bs, freemap, nblocks, dirsize, _res, bmaddr = struct.unpack_from('<IIIIII', d, 32)

def blocks(idx_list):
    return b''.join(d[i * bs:(i + 1) * bs] for i in idx_list)

ndb = (dirsize + bs - 1) // bs
dirblocksidx = struct.unpack_from('<%dI' % ndb, d, bmaddr * bs)
dr = blocks(dirblocksidx)[:dirsize]
nstreams = struct.unpack_from('<I', dr, 0)[0]
sizes = struct.unpack_from('<%dI' % nstreams, dr, 4)
off = 4 + 4 * nstreams
streams = []
for s in sizes:
    if s == 0xFFFFFFFF:
        streams.append(b''); continue
    n = (s + bs - 1) // bs
    idx = struct.unpack_from('<%dI' % n, dr, off); off += 4 * n
    streams.append(blocks(idx)[:s])

tpi = streams[2]
ver, hsize, tmin, tmax, rbytes = struct.unpack_from('<IIIII', tpi, 0)
recs = {}
p = hsize
ti = tmin
while p < hsize + rbytes:
    ln, kind = struct.unpack_from('<HH', tpi, p)
    recs[ti] = (kind, tpi[p + 4:p + 2 + ln])
    p += 2 + ln
    ti += 1

BASIC = {0x70: 'char', 0x71: 'wchar', 0x20: 'uchar', 0x10: 'char', 0x11: 'short', 0x21: 'ushort', 0x12: 'long', 0x22: 'ulong',
         0x13: 'int64', 0x23: 'uint64', 0x74: 'int', 0x75: 'uint', 0x03: 'void', 0x30: 'bool', 0x40: 'float', 0x41: 'double',
         0x68: 'int8', 0x69: 'uint8', 0x72: 'short', 0x73: 'ushort', 0x76: 'int64', 0x77: 'uint64'}


def num(b, o):
    v = struct.unpack_from('<H', b, o)[0]
    if v < 0x8000:
        return v, o + 2
    if v == 0x8001: return struct.unpack_from('<h', b, o + 2)[0], o + 4
    if v == 0x8002: return struct.unpack_from('<H', b, o + 2)[0], o + 4
    if v == 0x8003: return struct.unpack_from('<i', b, o + 2)[0], o + 4
    if v == 0x8004: return struct.unpack_from('<I', b, o + 2)[0], o + 6
    return 0, o + 2


def cstr(b, o):
    e = b.index(b'\0', o)
    return b[o:e].decode('latin1'), e + 1


def tname(t):
    if t < 0x1000:
        base = BASIC.get(t & 0xFF, 'basic%x' % t)
        return base + ('*' if (t & 0xF00) else '')
    if t not in recs: return '?%x' % t
    k, b = recs[t]
    if k == 0x1002:  # pointer
        return tname(struct.unpack_from('<I', b, 0)[0]) + '*'
    if k == 0x1001:
        return tname(struct.unpack_from('<I', b, 0)[0])
    if k == 0x1503:
        et, it = struct.unpack_from('<II', b, 0)
        sz, o = num(b, 8)
        return '%s[bytes=%d]' % (tname(et), sz)
    if k in (0x1504, 0x1505, 0x1506):
        o = 4 + 4 + 4 + 4 if k != 0x1506 else 4 + 4
        if k != 0x1506:
            o = 2 + 2 + 4 + 4 + 4
        else:
            o = 2 + 2 + 4
        sz, o = num(b, o)
        n, o = cstr(b, o)
        return n
    if k == 0x1507:
        o = 2 + 2 + 4 + 4
        n, o = cstr(b, o)
        return 'enum ' + n
    return 'kind%x' % k


def fields(ti):
    out = []
    while ti and ti in recs:
        k, b = recs[ti]
        if k != 0x1203: break
        o = 0; nxt = 0
        while o < len(b):
            if b[o] >= 0xF0:
                o += b[o] & 0x0F; continue
            if o + 2 > len(b): break
            lk = struct.unpack_from('<H', b, o)[0]
            if lk == 0x150d:
                attr, t = struct.unpack_from('<HI', b, o + 2)
                v, o2 = num(b, o + 8)
                n, o = cstr(b, o2)
                out.append(('member', n, v, tname(t)))
            elif lk == 0x1404:
                nxt = struct.unpack_from('<I', b, o + 4)[0]; o += 8
            elif lk == 0x1502:
                attr = struct.unpack_from('<H', b, o + 2)[0]
                v, o2 = num(b, o + 4)
                n, o = cstr(b, o2)
                out.append(('enum', n, v, ''))
            else:
                out.append(('?', hex(lk), 0, '')); break
        ti = nxt
    return out


like = [n[len('--like='):] for n in names if n.startswith('--like=')]
if like:
    # Print every struct/class/union whose name contains the given text (handles typedef'd/underscored names).
    for t, (k, b) in recs.items():
        if k in (0x1504, 0x1505, 0x1506):
            if k == 0x1506:
                cnt, prop, fl = struct.unpack_from('<HHI', b, 0)
                sz, o = num(b, 8); n, _ = cstr(b, o)
            else:
                cnt, prop, fl, der, vs = struct.unpack_from('<HHIII', b, 0)
                sz, o = num(b, 16); n, _ = cstr(b, o)
            if like[0].lower() in n.lower():
                print('== %s (kind %x) size=%d fwdref=%s fieldlist=%x' % (n, k, sz, bool(prop & 0x80), fl))
                if fl and not (prop & 0x80):
                    for f in fields(fl):
                        print('   ', f)
    sys.exit(0)

enum_probe = [n[len('--enum-with='):] for n in names if n.startswith('--enum-with=')]
if enum_probe:
    # Print every enum that contains the given enumerator (used to decode SDK error codes).
    for t, (k, b) in recs.items():
        if k == 0x1507:
            cnt, prop, utype, fl = struct.unpack_from('<HHII', b, 0)
            ename, _ = cstr(b, 12)
            if not fl or (prop & 0x80):
                continue
            fs = fields(fl)
            if any(f[1] == enum_probe[0] for f in fs):
                print('== enum', ename)
                for f in fs:
                    print('   %s = %d' % (f[1], f[2]))
    sys.exit(0)

seen = set()
for t, (k, b) in recs.items():
    if k in (0x1504, 0x1505, 0x1506, 0x1507):
        if k == 0x1507:
            cnt, prop, utype, fl = struct.unpack_from('<HHII', b, 0)
            sz = 0; n, _ = cstr(b, 12)
        elif k == 0x1506:
            cnt, prop, fl = struct.unpack_from('<HHI', b, 0)
            sz, o = num(b, 8); n, _ = cstr(b, o)
        else:
            cnt, prop, fl, der, vs = struct.unpack_from('<HHIII', b, 0)
            sz, o = num(b, 16); n, _ = cstr(b, o)
        if n in names and not (prop & 0x80) and n not in seen and fl:
            seen.add(n)
            print('== %s (kind %x) size=%d' % (n, k, sz))
            for f in fields(fl):
                print('   ', f)
