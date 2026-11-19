#!/usr/bin/env python3
"""Generates assets/app.ico (a folder with a green check mark).

The icon is produced without third-party libraries: each size is rendered as
raw RGBA pixels, encoded as a PNG with zlib, and packed into an ICO container.
Run from the repository root:  python3 tools/make_icon.py
"""

import os
import struct
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

FOLDER_BACK = (214, 176, 88, 255)
FOLDER_FRONT = (255, 214, 102, 255)
FOLDER_EDGE = (150, 112, 40, 255)
CHECK = (46, 160, 67, 255)
CHECK_EDGE = (20, 90, 35, 255)
OUTLINE = (90, 62, 20, 255)


def _blend(dst, src):
    """Alpha-blend src over dst (both RGBA tuples)."""
    sa = src[3] / 255.0
    if sa >= 1.0:
        return src
    da = dst[3] / 255.0
    out_a = sa + da * (1 - sa)
    if out_a == 0:
        return (0, 0, 0, 0)
    out = []
    for i in range(3):
        out.append(int(round((src[i] * sa + dst[i] * da * (1 - sa)) / out_a)))
    out.append(int(round(out_a * 255)))
    return tuple(out)


def _set(px, w, x, y, color):
    if 0 <= x < w and 0 <= y < w:
        idx = (y * w + x) * 4
        cur = (px[idx], px[idx + 1], px[idx + 2], px[idx + 3])
        r, g, b, a = _blend(cur, color)
        px[idx], px[idx + 1], px[idx + 2], px[idx + 3] = r, g, b, a


def _fill_rect(px, w, x0, y0, x1, y1, color):
    for y in range(max(0, y0), min(w, y1)):
        for x in range(max(0, x0), min(w, x1)):
            _set(px, w, x, y, color)


def _thick_line(px, w, p0, p1, color, thickness):
    (x0, y0), (x1, y1) = p0, p1
    steps = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
    half = thickness / 2.0
    for i in range(steps + 1):
        t = i / steps
        cx = x0 + (x1 - x0) * t
        cy = y0 + (y1 - y0) * t
        for dy in range(int(-half) - 1, int(half) + 2):
            for dx in range(int(-half) - 1, int(half) + 2):
                if (dx * dx + dy * dy) ** 0.5 <= half:
                    _set(px, w, int(round(cx)) + dx, int(round(cy)) + dy, color)


def render(size):
    px = bytearray(size * size * 4)
    s = size / 32.0  # design grid is 32x32

    def u(v):
        return int(round(v * s))

    # Folder tab.
    _fill_rect(px, size, u(3), u(6), u(14), u(11), FOLDER_EDGE)
    _fill_rect(px, size, u(4), u(7), u(13), u(10), FOLDER_BACK)

    # Folder body with a one-pixel outline.
    _fill_rect(px, size, u(3), u(10), u(29), u(26), OUTLINE)
    _fill_rect(px, size, u(4), u(11), u(28), u(25), FOLDER_FRONT)

    # Slight shading on the bottom of the body.
    _fill_rect(px, size, u(4), u(21), u(28), u(25), FOLDER_BACK)

    # Check mark.
    thickness = max(1.0, 2.6 * s)
    _thick_line(px, size, (u(11), u(18)), (u(15), u(22)), CHECK_EDGE, thickness + 1)
    _thick_line(px, size, (u(15), u(22)), (u(23), u(13)), CHECK_EDGE, thickness + 1)
    _thick_line(px, size, (u(11), u(18)), (u(15), u(22)), CHECK, thickness)
    _thick_line(px, size, (u(15), u(22)), (u(23), u(13)), CHECK, thickness)

    return bytes(px)


def png(size, rgba):
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filter type 0
        raw.extend(rgba[y * stride:(y + 1) * stride])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def main():
    images = [(size, png(size, render(size))) for size in SIZES]

    out = bytearray()
    out += struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for size, data in images:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in images:
        out += data

    target = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "app.ico")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "wb") as handle:
        handle.write(out)
    print("wrote", target, len(out), "bytes")


if __name__ == "__main__":
    main()
