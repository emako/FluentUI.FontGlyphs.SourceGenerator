"""Write Assets/SampleIcons.ttf: one private-use square named ic_fluent_add_20_regular."""
import struct
from pathlib import Path


def u16(n):
    return struct.pack(">H", n & 0xFFFF)


def u32(n):
    return struct.pack(">I", n & 0xFFFFFFFF)


def i16(n):
    return struct.pack(">h", n)


def checksum(data):
    padded = data + b"\0" * ((4 - len(data) % 4) % 4)
    total = 0
    for i in range(0, len(padded), 4):
        total = (total + struct.unpack(">I", padded[i : i + 4])[0]) & 0xFFFFFFFF
    return total


def glyph_square():
    # (200,200) -> (400,200) -> (400,400) -> (200,400)
    return b"".join(
        [
            i16(1),
            i16(200),
            i16(200),
            i16(400),
            i16(400),
            u16(3),
            u16(0),
            bytes([0x33, 0x33, 0x35, 0x23, 200, 200, 200, 200, 200]),
        ]
    )


def build():
    def even(data):
        return data + (b"\0" if len(data) % 2 else b"")

    glyf0 = even(b"".join([i16(0), i16(0), i16(0), i16(0), i16(0)]))
    glyf1 = even(glyph_square())
    glyf = glyf0 + glyf1
    loca = u16(0) + u16(len(glyf0) // 2) + u16(len(glyf) // 2)

    # cmap format 4: U+E000 -> glyph 1
    fmt4 = b"".join(
        [
            u16(4),
            u16(34),
            u16(0),
            u16(4),
            u16(4),
            u16(1),
            u16(0),
            u16(0xE000),
            u16(0xFFFF),
            u16(0),
            u16(0xE000),
            u16(0xFFFF),
            i16(0),
            i16(1),
            u16(4),
            u16(0),
            u16(1),
        ]
    )
    cmap = b"".join([u16(0), u16(1), u16(3), u16(1), u32(12), fmt4])

    name_strings = {
        1: "Sample Icons",
        2: "Regular",
        4: "Sample Icons",
        6: "SampleIcons",
    }
    records = []
    storage = b""
    for name_id, text in name_strings.items():
        encoded = text.encode("utf-16-be")
        records.append((name_id, len(storage), len(encoded)))
        storage += encoded
    name = b"".join(
        [
            u16(0),
            u16(len(records)),
            u16(6 + 12 * len(records)),
            b"".join(
                u16(3) + u16(1) + u16(0x0409) + u16(name_id) + u16(length) + u16(offset)
                for name_id, offset, length in records
            ),
            storage,
        ]
    )

    glyph_name = b"ic_fluent_add_20_regular"
    post = b"".join(
        [
            u32(0x00020000),
            u32(0),
            i16(0),
            i16(0),
            u32(0),
            u32(0),
            u32(0),
            u32(0),
            u32(0),
            u16(2),
            u16(0),
            u16(258),
            bytes([len(glyph_name)]),
            glyph_name,
        ]
    )

    head = bytearray(54)
    head[0:4] = u32(0x00010000)
    head[4:8] = u32(0x00010000)
    head[12:16] = u32(0x5F0F3CF5)
    head[16:18] = u16(0x000B)
    head[18:20] = u16(1000)
    head[36:38] = i16(0)
    head[38:40] = i16(0)
    head[40:42] = i16(1000)
    head[42:44] = i16(1000)
    head[44:46] = u16(0)
    head[46:48] = u16(8)
    head[48:50] = i16(2)
    head[50:52] = i16(0)
    head[52:54] = i16(0)

    hhea = bytearray(36)
    hhea[0:4] = u32(0x00010000)
    hhea[4:6] = i16(800)
    hhea[6:8] = i16(-200)
    hhea[8:10] = i16(0)
    hhea[10:12] = u16(1000)
    hhea[12:14] = i16(200)
    hhea[14:16] = i16(0)
    hhea[16:18] = i16(0)
    hhea[34:36] = u16(2)

    maxp = bytearray(32)
    maxp[0:4] = u32(0x00010000)
    maxp[4:6] = u16(2)
    maxp[6:8] = u16(400)
    maxp[8:10] = u16(1)
    maxp[10:12] = u16(4)
    maxp[12:14] = u16(0)
    maxp[14:16] = u16(1)
    maxp[16:18] = u16(4)
    maxp[26:28] = u16(2)
    maxp[28:30] = u16(1)
    maxp[30:32] = u16(0)

    os2 = bytearray(96)
    os2[0:2] = u16(4)
    os2[2:4] = i16(500)
    os2[4:6] = u16(400)
    os2[8:10] = i16(5)
    os2[32:34] = i16(800)
    os2[34:36] = i16(-200)
    os2[36:38] = i16(800)
    os2[42:44] = i16(8)
    os2[58:60] = u16(0)
    os2[60:62] = u16(0)
    os2[62:64] = u16(3)
    os2[64:66] = u16(0xE000)
    os2[66:68] = u16(0xE000)
    os2[78:80] = u16(800)
    os2[80:82] = i16(-200)

    hmtx = u16(1000) + i16(0) + u16(1000) + i16(200)

    tables = {
        "OS/2": bytes(os2),
        "cmap": cmap,
        "glyf": glyf,
        "head": bytes(head),
        "hhea": bytes(hhea),
        "hmtx": hmtx,
        "loca": loca,
        "maxp": bytes(maxp),
        "name": name,
        "post": post,
    }
    tags = sorted(tables)
    offset = 12 + 16 * len(tags)
    directory = []
    blobs = []
    for tag in tags:
        data = tables[tag]
        directory.append((tag, checksum(data), offset, len(data)))
        padded = data + b"\0" * ((4 - len(data) % 4) % 4)
        blobs.append(padded)
        offset += len(padded)

    header = b"".join([u32(0x00010000), u16(len(tags)), u16(128), u16(3), u16(32)])
    dir_bytes = b"".join(tag.encode("ascii") + u32(csum) + u32(off) + u32(length) for tag, csum, off, length in directory)
    font = header + dir_bytes + b"".join(blobs)
    whole = checksum(font)
    adjustment = (0xB1B0AFBA - whole) & 0xFFFFFFFF
    head_off = next(off for tag, _, off, _ in directory if tag == "head")
    font = font[: head_off + 8] + u32(adjustment) + font[head_off + 12 :]
    return font


if __name__ == "__main__":
    out = Path(__file__).with_name("Assets") / "SampleIcons.ttf"
    out.parent.mkdir(exist_ok=True)
    out.write_bytes(build())
    print(out, out.stat().st_size)
