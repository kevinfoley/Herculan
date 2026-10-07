"""Builds Branding/HE Icon.ico from every Branding/HE Icon <size>.png.

Each PNG goes in exactly as drawn, never rescaled. Entries are ordered largest first, the 256 px entry is stored as PNG
and the smaller ones as 32-bit bitmaps with an AND mask: the layout icon editors and Visual Studio produce.

    python tools/scripts/make_ico.py
"""
import glob
import io
import os
import struct
import sys

from PIL import Image

BRANDING = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Branding")


def bitmap_entry(image):
    """A 32-bit BGRA DIB, rows bottom-up, followed by a 1-bit AND mask set where the pixel is fully transparent."""
    width, height = image.size
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = image.load()
    colour = bytearray()
    for y in reversed(range(height)):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            colour += bytes((b, g, r, a))
    mask_stride = (width + 31) // 32 * 4
    mask = bytearray()
    for y in reversed(range(height)):
        row = bytearray(mask_stride)
        for x in range(width):
            if pixels[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row
    return header + bytes(colour) + bytes(mask)


def png_entry(image):
    out = io.BytesIO()
    image.save(out, format="PNG")
    return out.getvalue()


def main():
    paths = glob.glob(os.path.join(BRANDING, "HE Icon *.png"))
    images = [Image.open(p).convert("RGBA") for p in paths]
    for path, image in zip(paths, images):
        if image.width != image.height or image.width > 256:
            sys.exit(f"{os.path.basename(path)} is {image.width}x{image.height}; icons must be square and at most 256 px.")
    images.sort(key=lambda image: -image.width)

    entries = [png_entry(i) if i.width == 256 else bitmap_entry(i) for i in images]
    directory = bytearray()
    offset = 6 + 16 * len(images)
    for image, data in zip(images, entries):
        size = image.width % 256  # 0 means 256
        directory += struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(data), offset)
        offset += len(data)

    with open(os.path.join(BRANDING, "HE Icon.ico"), "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(images)))
        f.write(directory)
        for data in entries:
            f.write(data)
    print("HE Icon.ico:", ", ".join(f"{i.width}" for i in images))


if __name__ == "__main__":
    main()
