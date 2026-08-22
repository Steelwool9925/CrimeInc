#!/usr/bin/env python3
"""Generate the `crime` pack's art assets from its own palette (T4.1).

The MVP has no licensed art. Rather than commit opaque binaries, this script draws
`logo.png` and `icon.png` from the palette already declared in `pack.json`, so the
assets are reproducible, auditable and unambiguously licence-clean.

They are deliberately placeholders. The point of T4.1 is the *pipeline* — the pack
manifest names the assets, the API serves them, and the app renders whatever it is
given — so replacing these with sourced art is a content change with no code change.

Pure standard library: PNG is written by hand (zlib + CRC32), so there is no Pillow
or ImageMagick dependency.

Usage:
    python scripts/generate-pack-art.py [--pack content/crime]
"""

from __future__ import annotations

import argparse
import json
import pathlib
import struct
import sys
import zlib

# --- PNG encoding ---------------------------------------------------------


def _chunk(tag: bytes, data: bytes) -> bytes:
    """One PNG chunk: length, type, payload, CRC32 over type+payload."""
    return (
        struct.pack(">I", len(data))
        + tag
        + data
        + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    )


def encode_png(width: int, height: int, pixels: bytearray) -> bytes:
    """Encode an RGBA buffer as a truecolour-with-alpha PNG (bit depth 8, colour type 6)."""
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter type 0 (None) for every scanline
        raw += pixels[y * stride : (y + 1) * stride]
    return (
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + _chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + _chunk(b"IEND", b"")
    )


# --- a very small raster canvas -------------------------------------------

# 5x7 bitmap font, only the glyphs the wordmark needs.
FONT: dict[str, tuple[str, ...]] = {
    "D": ("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "I": ("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
    "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
    "M": ("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
    "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
    "O": ("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
    "P": ("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
    "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
    "U": ("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
    "W": ("10001", "10001", "10001", "10101", "10101", "11011", "10001"),
    " ": ("00000", "00000", "00000", "00000", "00000", "00000", "00000"),
}

GLYPH_W, GLYPH_H = 5, 7

Colour = tuple[int, int, int, int]


class Canvas:
    """A fixed-size RGBA raster with just the primitives these two assets need."""

    def __init__(self, width: int, height: int, background: Colour) -> None:
        self.width = width
        self.height = height
        self.px = bytearray(bytes(background) * (width * height))

    def _blend(self, x: int, y: int, colour: Colour) -> None:
        """Source-over blend of one pixel; ignores writes outside the canvas."""
        if not (0 <= x < self.width and 0 <= y < self.height):
            return
        sr, sg, sb, sa = colour
        if sa == 0:
            return
        i = (y * self.width + x) * 4
        if sa == 255:
            self.px[i : i + 4] = bytes(colour)
            return
        a = sa / 255.0
        for k, s in enumerate((sr, sg, sb)):
            self.px[i + k] = int(s * a + self.px[i + k] * (1 - a))
        self.px[i + 3] = max(self.px[i + 3], sa)

    def rect(self, x: int, y: int, w: int, h: int, colour: Colour) -> None:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self._blend(xx, yy, colour)

    def rounded_rect(self, x: int, y: int, w: int, h: int, r: int, colour: Colour) -> None:
        """Rounded rectangle, antialiased on the corner arcs only."""
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                dx = min(xx - (x + r), 0) or max(xx - (x + w - 1 - r), 0)
                dy = min(yy - (y + r), 0) or max(yy - (y + h - 1 - r), 0)
                if dx == 0 and dy == 0:
                    self._blend(xx, yy, colour)
                    continue
                d = (dx * dx + dy * dy) ** 0.5
                if d <= r - 0.5:
                    self._blend(xx, yy, colour)
                elif d < r + 0.5:  # feather the arc edge
                    cov = r + 0.5 - d
                    self._blend(xx, yy, (*colour[:3], int(colour[3] * cov)))

    def disc(self, cx: int, cy: int, radius: int, colour: Colour) -> None:
        for yy in range(cy - radius - 1, cy + radius + 2):
            for xx in range(cx - radius - 1, cx + radius + 2):
                d = ((xx - cx) ** 2 + (yy - cy) ** 2) ** 0.5
                if d <= radius - 0.5:
                    self._blend(xx, yy, colour)
                elif d < radius + 0.5:
                    self._blend(xx, yy, (*colour[:3], int(colour[3] * (radius + 0.5 - d))))

    def text(self, s: str, x: int, y: int, scale: int, colour: Colour, tracking: int = 1) -> int:
        """Draw `s` in the 5x7 font at `scale`. Returns the width drawn, in pixels."""
        cursor = x
        for ch in s.upper():
            glyph = FONT.get(ch)
            if glyph is None:
                raise KeyError(f"no glyph for {ch!r}; add it to FONT")
            for row, bits in enumerate(glyph):
                for col, bit in enumerate(bits):
                    if bit == "1":
                        self.rect(
                            cursor + col * scale, y + row * scale, scale, scale, colour
                        )
            cursor += (GLYPH_W + tracking) * scale
        return cursor - x - tracking * scale

    def to_png(self) -> bytes:
        return encode_png(self.width, self.height, self.px)


def text_width(s: str, scale: int, tracking: int = 1) -> int:
    return len(s) * (GLYPH_W + tracking) * scale - tracking * scale


# --- palette --------------------------------------------------------------


def parse_hex(value: str, fallback: str) -> Colour:
    """`#RRGGBB` (or `#AARRGGBB`) to an RGBA tuple, falling back on anything unparseable."""
    raw = (value or "").lstrip("#")
    if len(raw) not in (6, 8):
        raw = fallback.lstrip("#")
    if len(raw) == 8:  # AARRGGBB
        a, r, g, b = (int(raw[i : i + 2], 16) for i in (0, 2, 4, 6))
        return (r, g, b, a)
    r, g, b = (int(raw[i : i + 2], 16) for i in (0, 2, 4))
    return (r, g, b, 255)


def mix(a: Colour, b: Colour, t: float) -> Colour:
    """Linear blend between two opaque colours."""
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))  # type: ignore[return-value]


# --- the two assets -------------------------------------------------------


def draw_icon(primary: Colour, secondary: Colour, accent: Colour, size: int = 256) -> bytes:
    """App icon: a dark rounded tile, a red disc, and an accent bar across it."""
    c = Canvas(size, size, (0, 0, 0, 0))
    c.rounded_rect(0, 0, size, size, size // 5, secondary)

    # A subtle lift toward the top so the tile does not read as flat black.
    for y in range(size // 2):
        t = 1 - (y / (size / 2))
        c.rect(0, y, size, 1, (*mix(secondary, primary, 0.22 * t)[:3], 40))

    # Disc sits slightly high, leaving room for the accent rule beneath it.
    cy = int(size * 0.44)
    c.disc(size // 2, cy, int(size * 0.29), primary)
    c.disc(size // 2, cy, int(size * 0.29) - max(2, size // 64), mix(primary, secondary, 0.35))

    scale = max(1, size // 40)
    w = text_width("UE", scale)
    c.text("UE", (size - w) // 2, int(cy - GLYPH_H * scale / 2), scale, (250, 250, 250, 255))

    # Rule underneath the emblem — never across the lettering, which reads as a strikethrough.
    bar_h = max(3, size // 24)
    c.rect(int(size * 0.26), int(size * 0.79), int(size * 0.48), bar_h, accent)
    return c.to_png()


def draw_logo(
    primary: Colour, secondary: Colour, accent: Colour, width: int = 512, height: int = 160
) -> bytes:
    """Wordmark: the pack name over an accent rule, on a dark plate."""
    c = Canvas(width, height, (0, 0, 0, 0))
    c.rounded_rect(0, 0, width, height, height // 8, secondary)
    c.rect(0, 0, width, max(2, height // 40), primary)

    top, bottom = "UNDERWORLD", "EMPIRE"
    ts = max(1, width // 78)
    bs = max(1, width // 58)

    ty = int(height * 0.24)
    c.text(top, (width - text_width(top, ts)) // 2, ty, ts, (245, 245, 245, 255))

    rule_y = ty + GLYPH_H * ts + max(4, height // 22)
    rule_w = int(width * 0.52)
    c.rect((width - rule_w) // 2, rule_y, rule_w, max(2, height // 50), accent)

    by = rule_y + max(8, height // 14)
    c.text(bottom, (width - text_width(bottom, bs)) // 2, by, bs, primary)
    return c.to_png()


# --- entry point ----------------------------------------------------------


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--pack",
        default="content/crime",
        type=pathlib.Path,
        help="pack folder holding pack.json (default: content/crime)",
    )
    args = ap.parse_args(argv)

    manifest_path = args.pack / "pack.json"
    if not manifest_path.is_file():
        print(f"error: no pack.json at {manifest_path}", file=sys.stderr)
        return 1

    # utf-8-sig, not utf-8: pack.json is hand-edited, and a Windows editor (or PowerShell's
    # Set-Content -Encoding utf8) will happily write a BOM that plain utf-8 decoding chokes on.
    # .NET's File.ReadAllText strips it silently, so the server would load a manifest this
    # script could not.
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    palette = manifest.get("palette", {})
    primary = parse_hex(palette.get("primary", ""), "#B71C1C")
    secondary = parse_hex(palette.get("secondary", ""), "#212121")
    accent = parse_hex(palette.get("accent", ""), "#FFC107")

    # Write each asset to the path the manifest itself declares, so the manifest
    # stays the single source of truth for where art lives.
    assets = manifest.get("assets", {})
    targets = {
        "icon": (assets.get("icon", "assets/icon.png"), draw_icon(primary, secondary, accent)),
        "logo": (assets.get("logo", "assets/logo.png"), draw_logo(primary, secondary, accent)),
    }

    for name, (rel, data) in sorted(targets.items()):
        out = args.pack / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(data)
        print(f"wrote {out}  ({len(data):,} bytes)  [{name}]")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
