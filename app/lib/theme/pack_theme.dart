import 'package:flutter/material.dart';

import '../api/models.dart';

/// Builds the app's theme from the active content pack (T4.1).
///
/// The engine ships no palette of its own: colours come from `pack.json`, so re-theming for a
/// different pack is a content edit. Before this, `main.dart` seeded everything from
/// `Colors.deepPurple` while the shipped `crime` pack declared red — the manifest was written
/// but only one app bar ever read it.
///
/// Reworked for the "Steamnoir" pack (brass / near-black warm brown / dark teal): the theme is
/// now dark by default, since the shipped palette is designed for a dark scaffold with a light
/// (cream) text colour, not a light Material scheme with a brass seed.

/// Fallbacks used when the pack has not loaded yet, or declares an unparseable colour. Chosen to
/// match the shipped `crime` pack (Steamnoir: brass / near-black warm brown / dark teal) so the
/// pre-load frame does not flash a different palette.
const Color _fallbackPrimary = Color(0xFFC17A3D);
const Color _fallbackSecondary = Color(0xFF17110C);
const Color _fallbackAccent = Color(0xFF2B7A72);

/// Warm cream used for text/icons that must read on the dark secondary background. Not pack
/// data — every pack is expected to be dark enough that a light "on" colour reads on it, same
/// as `_fallbackPrimary` et al. are stand-ins for pack data before it loads.
const Color _cream = Color(0xFFEFE6D8);

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

  // Card/surface tone is derived from secondary rather than hardcoded, so a different pack's
  // dark secondary still produces a sensibly lighter card colour instead of an unrelated hex.
  final surface = Color.lerp(secondary, Colors.white, 0.08)!;

  // fromSeed generates a full, contrast-checked dark scheme; the manifest colours are then
  // pinned over it so the pack's declared values appear exactly as written rather than being
  // harmonised into something else. Everything the manifest does not specify stays derived.
  final scheme = ColorScheme.fromSeed(
    seedColor: primary,
    brightness: Brightness.dark,
  ).copyWith(
    primary: primary,
    secondary: secondary,
    tertiary: accent,
    // Brass is light enough that dark (near-black) text reads on it.
    onPrimary: secondary,
    onSecondary: _cream,
    onTertiary: _cream,
    onSurface: _cream,
    surface: surface,
  );

  return ThemeData(
    colorScheme: scheme,
    brightness: Brightness.dark,
    scaffoldBackgroundColor: secondary,
    cardColor: surface,
    // Material 3's Card widget does not read ThemeData.cardColor at all — it reads
    // cardTheme.color, falling back to colorScheme.surfaceContainerLow when unset. Without this,
    // every Card in the app renders an M3-seed-derived colour instead of the pack's surface.
    cardTheme: CardThemeData(color: surface),
    // Deliberate change from the old brass app bar: Steamnoir's reference design uses a dark
    // app bar that matches the scaffold, with brass reserved for accents/selection states.
    appBarTheme: AppBarTheme(
      backgroundColor: secondary,
      foregroundColor: _cream,
      elevation: 0,
    ),
    // Not consumed yet — a bottom nav shell lands in a later milestone — but the tab bar's
    // colours belong to "the theme," so they're defined here alongside everything else.
    bottomNavigationBarTheme: BottomNavigationBarThemeData(
      backgroundColor: secondary,
      selectedItemColor: primary,
      unselectedItemColor: _cream.withValues(alpha: 0.6),
      type: BottomNavigationBarType.fixed,
    ),
    // Same story as bottomNavigationBarTheme: for a later milestone's sidebar on wider layouts.
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: secondary,
      // NavigationRail defaults labelType to none, which renders every destination's label via
      // Visibility.maintain(visible: false, ...) — present for semantics but never painted. That
      // left the two label text styles below dead code and the side rail showing bare icons.
      labelType: NavigationRailLabelType.all,
      selectedIconTheme: IconThemeData(color: primary),
      selectedLabelTextStyle: TextStyle(color: primary),
      unselectedIconTheme: IconThemeData(color: _cream.withValues(alpha: 0.6)),
      unselectedLabelTextStyle: TextStyle(color: _cream.withValues(alpha: 0.6)),
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: accent),
    // Approximates a small-caps/serif-feeling header for titles only, without a new font asset
    // or pubspec dependency. Body text is left alone.
    textTheme: const TextTheme(
      titleLarge: TextStyle(letterSpacing: 1.1, fontWeight: FontWeight.w600),
      titleMedium: TextStyle(letterSpacing: 1.1, fontWeight: FontWeight.w600),
      headlineSmall: TextStyle(letterSpacing: 1.1, fontWeight: FontWeight.w600),
    ),
  );
}
