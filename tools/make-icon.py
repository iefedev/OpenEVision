"""Draws the app icon (a dark keyboard with colored keys, the same glyph as the tray icon) and writes
app/Resources/app.ico with 16, 24, 32, 48, 64, 128 and 256 px images. Standard library only.

    python tools/make-icon.py
"""
import os
import struct
import zlib

# Geometry on a 32x32 grid, matching TrayIcon.MakeIcon.
BODY = (1, 7, 31, 25)          # x0, y0, x1, y1
BODY_FILL = (40, 40, 40, 255)
EDGE = (210, 210, 210, 255)
KEY_COLORS = [(255, 0, 64), (255, 160, 0), (0, 220, 80), (0, 160, 255), (180, 0, 255)]


def draw(size):
    s = size / 32.0
    px = [[(0, 0, 0, 0)] * size for _ in range(size)]

    def rect(x0, y0, x1, y1, color):
        for y in range(max(0, round(y0 * s)), min(size, round(y1 * s))):
            row = px[y]
            for x in range(max(0, round(x0 * s)), min(size, round(x1 * s))):
                row[x] = color

    edge = max(1.0, 1.5 * s) / s
    x0, y0, x1, y1 = BODY
    rect(x0, y0, x1, y1, EDGE)
    rect(x0 + edge, y0 + edge, x1 - edge, y1 - edge, BODY_FILL)
    for r in range(3):
        for c in range(5):
            kx, ky = 4 + c * 5, 10 + r * 5
            rect(kx, ky, kx + 4, ky + 3, KEY_COLORS[(c + r) % 5] + (255,))
    return px


def png(px):
    h, w = len(px), len(px[0])
    raw = b"".join(b"\x00" + b"".join(bytes(p) for p in row) for row in px)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [png(draw(n)) for n in sizes]
    header = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b""
    for n, data in zip(sizes, images):
        entries += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "app", "Resources", "app.ico")
    with open(out, "wb") as f:
        f.write(header + entries + b"".join(images))
    print("wrote", os.path.normpath(out))


if __name__ == "__main__":
    main()
