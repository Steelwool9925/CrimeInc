import 'package:flutter/material.dart';

import '../api/models.dart';

/// Builds the app's theme from the active content pack (T4.1).
///
/// The engine ships no palette of its own: colours come from `pack.json`, so re-theming for a
/// different pack is a content edit. Before this, `main.dart` seeded everything from
/// `Colors.deepPurple` while the shipped `crime` pack declared red — the manifest was written
/// but only one app bar ever read it.
///
/// Reworked again for the "Steamnoir" pack's comic-noir pass: near-monochrome (ink black / bone
/// white / smoke grey) with exactly one spot colour (blood red) carried by the pack's primary,
/// used only where it means something -- primary actions, danger/error text, the selected nav
/// item. Everything else stays achromatic on purpose; a second accent hue would break the "one
/// spot colour" read the whole palette is built around.

/// Fallbacks used when the pack has not loaded yet, or declares an unparseable colour. Chosen to
/// match the shipped `crime` pack (Steamnoir: blood red / ink black / dried-blood accent) so the
/// pre-load frame does not flash a different palette.
const Color _fallbackPrimary = Color(0xFFC81E2C);
const Color _fallbackSecondary = Color(0xFF0A0A0C);
const Color _fallbackAccent = Color(0xFF7A1219);

/// Warm bone white used for text/icons and every ink-outline border. Not pack data — every pack
/// is expected to be dark enough that a light "on" colour reads on it, same as `_fallbackPrimary`
/// et al. are stand-ins for pack data before it loads.
const Color _bone = Color(0xFFEDE7DA);

/// Muted secondary text (captions, timestamps, "coming soon" labels) — dim enough to read as
/// background information without competing with `_bone` body text.
const Color _smoke = Color(0xFF8B8680);

/// Ink-outline width shared by every card, button and panel border, so the line weight reads as
/// one consistent pen stroke across the app rather than a mix of hairlines and thick strokes.
const double _inkWidth = 2.5;

/// Square corners everywhere: a comic panel border doesn't round off, and it's the single biggest
/// visual difference from Material's default soft-elevation Card.
const _sharpCorners = RoundedRectangleBorder(borderRadius: BorderRadius.zero);

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
    // Blood red is dark enough itself that bone (not near-black) is what reads on it.
    onPrimary: _bone,
    onSecondary: _bone,
    onTertiary: _bone,
    onSurface: _bone,
    surface: surface,
    // One spot colour, not two reds: fromSeed would otherwise generate its own independent
    // error red, which reads as a second, slightly-off accent hue next to the pack's primary.
    error: primary,
    onError: _bone,
  );

  return ThemeData(
    colorScheme: scheme,
    brightness: Brightness.dark,
    scaffoldBackgroundColor: secondary,
    cardColor: surface,
    // Material 3's Card widget does not read ThemeData.cardColor at all — it reads
    // cardTheme.color, falling back to colorScheme.surfaceContainerLow when unset. Without this,
    // every Card in the app renders an M3-seed-derived colour instead of the pack's surface.
    //
    // shape/side/elevation turn every Card into an ink-outline comic panel: a square-cornered
    // bone border with no soft Material elevation shadow behind it. This is the single biggest
    // driver of the "comic panel" read, and it's theme-level -- every screen's Cards (meters,
    // cash, progression, dashboard tiles) get it for free, no per-widget changes.
    cardTheme: CardThemeData(
      color: surface,
      elevation: 0,
      shape: _sharpCorners.copyWith(side: const BorderSide(color: _bone, width: _inkWidth)),
    ),
    // ElevatedButton is this app's primary-action button (Retry, Continue, Login/Register
    // submit) -- filled with the pack's one spot colour and square-cornered, same ink-outline
    // language as the cards. OutlinedButton is secondary (Change Server, Skills, Cancel) --
    // bone outline, no fill, so the one filled-red button per screen actually stands out.
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: primary,
        foregroundColor: _bone,
        elevation: 0,
        shape: _sharpCorners,
        side: const BorderSide(color: _bone, width: _inkWidth),
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: _bone,
        side: const BorderSide(color: _bone, width: _inkWidth),
        shape: _sharpCorners,
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
      ),
    ),
    // Steamnoir's app bar matches the scaffold, with the spot colour reserved for actions/state.
    appBarTheme: AppBarTheme(
      backgroundColor: secondary,
      foregroundColor: _bone,
      elevation: 0,
    ),
    // Not consumed yet — a bottom nav shell lands in a later milestone — but the tab bar's
    // colours belong to "the theme," so they're defined here alongside everything else.
    bottomNavigationBarTheme: BottomNavigationBarThemeData(
      backgroundColor: secondary,
      selectedItemColor: primary,
      unselectedItemColor: _smoke,
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
      unselectedIconTheme: const IconThemeData(color: _smoke),
      unselectedLabelTextStyle: const TextStyle(color: _smoke),
    ),
    // Neutral, not the spot colour: a loading spinner isn't a danger/CTA state, and painting it
    // red would spend the one accent colour on something that shows up constantly.
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: _bone),
    // Bebas Neue (bundled locally, see pubspec.yaml) on every display/title role: the "chunky
    // comic lettering" signature, applied once at the theme level so every screen's headers pick
    // it up automatically. Body text keeps the platform default -- comic lettering at body size
    // reads as a novelty, not a design system.
    textTheme: const TextTheme(
      displayLarge: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      displayMedium: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      displaySmall: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      headlineLarge: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      headlineMedium: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      headlineSmall: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      titleLarge: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.5),
      titleMedium: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.8),
      titleSmall: TextStyle(fontFamily: 'Bebas Neue', letterSpacing: 0.8),
    ),
  );
}

/// Tabular, monospaced style for numbers the player is meant to read as data -- cash, XP,
/// level, stat totals, countdowns. Applied per-widget (not a global [TextTheme] role) because
/// ordinary titles and stat numbers both currently borrow the same roles (e.g. `titleLarge`);
/// forcing every title into mono would also catch headers that are not numbers.
TextStyle statNumberStyle(BuildContext context, {double? fontSize, FontWeight? fontWeight}) {
  return TextStyle(
    fontFamily: 'monospace',
    fontFamilyFallback: const ['Courier New'],
    fontFeatures: const [FontFeature.tabularFigures()],
    fontSize: fontSize,
    fontWeight: fontWeight ?? FontWeight.w600,
    color: Theme.of(context).colorScheme.onSurface,
  );
}
