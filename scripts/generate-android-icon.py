#!/usr/bin/env python3
"""Generate the Android launcher icon from the pack's own palette.

The launcher icon (`app/android/app/src/main/res/mipmap-*/ic_launcher.png`) is a
compiled asset baked into the APK at build time — unlike `content/crime/assets/`,
it is not served at runtime, so `generate-pack-art.py` never touched it. It was
still the stock Flutter template icon as of 2026-09-07, despite the rest of the
app's Steamnoir branding.

Reuses `generate-pack-art.py`'s `draw_icon` (same palette, same design) at each of
the five legacy launcher-icon densities, so there is exactly one icon design in
this repo, not two to keep in sync by hand. Pure standard library, same as its
sibling script.

Usage:
    python scripts/generate-android-icon.py [--pack content/crime] [--app app]
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import pathlib
import sys

# Legacy (non-adaptive) launcher-icon densities Android expects, in px.
DENSITIES: dict[str, int] = {
    "mipmap-mdpi": 48,
    "mipmap-hdpi": 72,
    "mipmap-xhdpi": 96,
    "mipmap-xxhdpi": 144,
    "mipmap-xxxhdpi": 192,
}


def _load_pack_art_module(script_dir: pathlib.Path):
    """Import generate-pack-art.py by path — its filename has a hyphen, so it is
    not a valid module name for a plain `import`."""
    spec = importlib.util.spec_from_file_location(
        "generate_pack_art", script_dir / "generate-pack-art.py"
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--pack", default="content/crime", type=pathlib.Path,
        help="pack folder holding pack.json, for the palette (default: content/crime)",
    )
    ap.add_argument(
        "--app", default="app", type=pathlib.Path,
        help="Flutter app folder holding android/ (default: app)",
    )
    args = ap.parse_args(argv)

    manifest_path = args.pack / "pack.json"
    if not manifest_path.is_file():
        print(f"error: no pack.json at {manifest_path}", file=sys.stderr)
        return 1

    res_dir = args.app / "android" / "app" / "src" / "main" / "res"
    if not res_dir.is_dir():
        print(f"error: no Android res/ dir at {res_dir}", file=sys.stderr)
        return 1

    pack_art = _load_pack_art_module(pathlib.Path(__file__).resolve().parent)

    # utf-8-sig for the same reason generate-pack-art.py reads it that way —
    # a Windows-written pack.json may carry a BOM.
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    palette = manifest.get("palette", {})
    primary = pack_art.parse_hex(palette.get("primary", ""), "#B71C1C")
    secondary = pack_art.parse_hex(palette.get("secondary", ""), "#212121")
    accent = pack_art.parse_hex(palette.get("accent", ""), "#FFC107")

    for mipmap_dir, size in DENSITIES.items():
        out = res_dir / mipmap_dir / "ic_launcher.png"
        out.parent.mkdir(parents=True, exist_ok=True)
        data = pack_art.draw_icon(primary, secondary, accent, size=size)
        out.write_bytes(data)
        print(f"wrote {out}  ({size}x{size}, {len(data):,} bytes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
