# `crime` content pack

The MVP theme pack. Populated from M1 onward with the data-driven content schema:

- `pack.json` — theme name, currency labels, palette, asset map
- `jobs.json` — job catalog (M2)
- `properties.json` — property catalog (M3)
- `items.json` — item catalog (M3)
- `strings.json` — localizable UI text

The engine references abstract ids; this pack supplies concrete content + art paths. Swapping
assets or text here must **not** require code changes.
