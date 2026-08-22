import 'package:flutter/material.dart';

import '../api/models.dart';

/// Builds the app's theme from the active content pack (T4.1).
///
/// The engine ships no palette of its own: colours come from `pack.json`, so re-theming for a
/// different pack is a content edit. Before this, `main.dart` seeded everything from
/// `Colors.deepPurple` while the shipped `crime` pack declared red — the manifest was written
/// but only one app bar ever read it.

/// Fallbacks used when the pack has not loaded yet, or declares an unparseable colour. Chosen to
/// match the shipped `crime` pack so the pre-load frame does not flash a different palette.
const Color _fallbackPrimary = Color(0xFFB71C1C);
const Color _fallbackSecondary = Color(0xFF212121);
const Color _fallbackAccent = Color(0xFFFFC107);

/// Parses `#RRGGBB`, `RRGGBB`, or `#AARRGGBB` into a [Color].
///
/// Returns [fallback] for anything it cannot parse. A bad colour in a content pack should leave
/// the app looking wrong, never crash it — the pack is data, and data can be malformed.
Color parsePackColor(String? hex, Color fallback) {
  var raw = (hex ?? '').trim();
  if (raw.startsWith('#')) raw = raw.substring(1);
  if (raw.length != 6 && raw.length != 8) return fallback;

  final value = int.tryParse(raw, radix: 16);
  if (value == null) return fallback;

  // Six digits carry no alpha, so assume fully opaque.
  return Color(raw.length == 6 ? 0xFF000000 | value : value);
}

/// The [ThemeData] for [pack], or the fallback palette when it has not loaded.
ThemeData packTheme(ContentPack? pack) {
  final primary = parsePackColor(pack?.primaryColorHex, _fallbackPrimary);
  final secondary = parsePackColor(pack?.secondaryColorHex, _fallbackSecondary);
  final accent = parsePackColor(pack?.accentColorHex, _fallbackAccent);

  // fromSeed generates a full, contrast-checked scheme; the three manifest colours are then
  // pinned over it so the pack's declared values appear exactly as written rather than being
  // harmonised into something else. Everything the manifest does not specify stays derived.
  final scheme = ColorScheme.fromSeed(seedColor: primary).copyWith(
    primary: primary,
    secondary: secondary,
    tertiary: accent,
  );

  return ThemeData(
    colorScheme: scheme,
    appBarTheme: AppBarTheme(
      backgroundColor: primary,
      foregroundColor: scheme.onPrimary,
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: accent),
  );
}
