import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/theme/pack_theme.dart';

/// T4.1: the pack manifest drives the app's colours and art, not hardcoded constants.
///
/// Before this, main.dart seeded the whole app from Colors.deepPurple and only the Home app bar
/// read the pack's primary — so the shipped "crime" pack declared a red palette and the app
/// rendered purple. These tests pin the manifest as the source of truth.
void main() {
  ContentPack packWith({
    String primary = '#B71C1C',
    String secondary = '#212121',
    String accent = '#FFC107',
    Map<String, String> assets = const {'logo': 'assets/logo.png'},
  }) =>
      ContentPack(
        name: 'Underworld Empire',
        cashLabel: 'Cash',
        premiumLabel: 'Gold Bars',
        primaryColorHex: primary,
        secondaryColorHex: secondary,
        accentColorHex: accent,
        assets: assets,
      );

  group('ContentPack.fromJson', () {
    test('reads the full palette, not just the primary', () {
      final pack = ContentPack.fromJson({
        'name': 'Underworld Empire',
        'palette': {
          'primary': '#B71C1C',
          'secondary': '#212121',
          'accent': '#FFC107',
        },
      });

      expect(pack.primaryColorHex, '#B71C1C');
      expect(pack.secondaryColorHex, '#212121');
      expect(pack.accentColorHex, '#FFC107');
    });

    test('reads the asset map the manifest declares', () {
      final pack = ContentPack.fromJson({
        'assets': {'logo': 'assets/logo.png', 'icon': 'assets/icon.png'},
      });

      expect(pack.assets, {'logo': 'assets/logo.png', 'icon': 'assets/icon.png'});
    });

    test('a pack declaring no assets yields an empty map rather than a null', () {
      expect(ContentPack.fromJson(const {}).assets, isEmpty);
    });

    test('hasAsset reports what the manifest actually declares', () {
      final pack = ContentPack.fromJson({
        'assets': {'logo': 'assets/logo.png'},
      });

      expect(pack.hasAsset('logo'), isTrue);
      expect(pack.hasAsset('icon'), isFalse);
    });

    test('an asset declared but left blank does not count as present', () {
      final pack = ContentPack.fromJson({
        'assets': {'logo': ''},
      });

      expect(pack.hasAsset('logo'), isFalse);
    });
  });

  group('parsePackColor', () {
    test('parses a six-digit hex with a leading hash', () {
      expect(parsePackColor('#B71C1C', Colors.black), const Color(0xFFB71C1C));
    });

    test('parses a six-digit hex without a leading hash', () {
      expect(parsePackColor('B71C1C', Colors.black), const Color(0xFFB71C1C));
    });

    test('parses an eight-digit hex as ARGB', () {
      expect(parsePackColor('#80B71C1C', Colors.black), const Color(0x80B71C1C));
    });

    test('falls back rather than throwing on a malformed value', () {
      expect(parsePackColor('not-a-colour', Colors.black), Colors.black);
    });

    test('falls back on an empty value', () {
      expect(parsePackColor('', Colors.black), Colors.black);
    });
  });

  group('packTheme', () {
    test('takes its primary straight from the manifest', () {
      final theme = packTheme(packWith(primary: '#B71C1C'));

      expect(theme.colorScheme.primary, const Color(0xFFB71C1C));
    });

    test('takes its secondary and accent from the manifest too', () {
      final theme = packTheme(packWith(secondary: '#212121', accent: '#FFC107'));

      expect(theme.colorScheme.secondary, const Color(0xFF212121));
      expect(theme.colorScheme.tertiary, const Color(0xFFFFC107));
    });

    test('a different pack yields a different primary', () {
      // The swap-without-a-code-change contract, at the palette level.
      final green = packTheme(packWith(primary: '#00FF00'));

      expect(green.colorScheme.primary, const Color(0xFF00FF00));
    });

    test('renders a usable theme before the pack has loaded', () {
      final theme = packTheme(null);

      expect(theme, isA<ThemeData>());
      expect(theme.colorScheme.primary, isNotNull);
    });

    test('a pack with a malformed palette still yields a usable theme', () {
      final theme = packTheme(packWith(primary: 'garbage', secondary: '', accent: '#'));

      expect(theme, isA<ThemeData>());
      expect(theme.colorScheme.primary, isNotNull);
      // Steamnoir is a dark theme end-to-end: a malformed pack must not silently drop back to
      // a light scheme, it should fall back to the dark defaults.
      expect(theme.brightness, Brightness.dark);
    });

    test('is not the old hardcoded deepPurple seed', () {
      // Guards the regression this task exists to fix.
      final theme = packTheme(packWith(primary: '#B71C1C'));
      final oldDefault = ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple),
      );

      expect(theme.colorScheme.primary, isNot(oldDefault.colorScheme.primary));
    });

    group('Steamnoir dark theme', () {
      test('is a dark theme', () {
        final theme = packTheme(packWith());

        expect(theme.brightness, Brightness.dark);
      });

      test('scaffold background is the pack\'s parsed secondary colour', () {
        final theme = packTheme(packWith(secondary: '#17110C'));

        expect(theme.scaffoldBackgroundColor, const Color(0xFF17110C));
      });

      test('card colour is a lightened derivative of secondary, not the flat scaffold colour',
          () {
        final theme = packTheme(packWith(secondary: '#17110C'));

        expect(theme.cardColor, isNot(theme.scaffoldBackgroundColor));
      });

      test('onSurface text colour is distinct from the scaffold background', () {
        // Basic contrast sanity check: text must not be the same colour as the background it
        // sits on.
        final theme = packTheme(packWith());

        expect(theme.colorScheme.onSurface, isNot(theme.scaffoldBackgroundColor));
      });
    });

    group('comic-noir chrome', () {
      test('cards render as ink-outline panels: sharp corners, visible bone border', () {
        final theme = packTheme(packWith());

        final shape = theme.cardTheme.shape;
        expect(shape, isA<RoundedRectangleBorder>());
        final rect = shape as RoundedRectangleBorder;
        // Comic panels are square-cornered -- Material's default rounded Card would read as
        // a soft app tile, not an inked panel border.
        expect(rect.borderRadius, BorderRadius.zero);
        expect(rect.side.width, greaterThanOrEqualTo(2));
        expect(theme.cardTheme.elevation, 0);
      });

      test('elevated buttons (primary actions) use the pack primary as a filled, square CTA',
          () {
        final theme = packTheme(packWith(primary: '#C81E2C'));
        final style = theme.elevatedButtonTheme.style!;

        expect(style.backgroundColor?.resolve({}), const Color(0xFFC81E2C));
        final shape = style.shape?.resolve({}) as RoundedRectangleBorder?;
        expect(shape?.borderRadius, BorderRadius.zero);
      });

      test('outlined buttons (secondary actions) get a visible ink-outline border, not a hairline',
          () {
        final theme = packTheme(packWith());
        final style = theme.outlinedButtonTheme.style!;

        final side = style.side?.resolve({});
        expect(side, isNotNull);
        expect(side!.width, greaterThanOrEqualTo(2));
        final shape = style.shape?.resolve({}) as RoundedRectangleBorder?;
        expect(shape?.borderRadius, BorderRadius.zero);
      });

      test('danger text is the primary hue, lightened only as far as legibility needs', () {
        // Two rules pulling against each other, and the colour has to satisfy both.
        //
        // 1. Colour discipline. A second, independently generated "error red" from
        //    ColorScheme.fromSeed reads as a slightly-off second accent and would quietly break
        //    the one-spot-colour design. The danger colour must stay the pack's own hue.
        // 2. Legibility. Every use of `error` in this app is *text* — gate labels ("Requires
        //    level 3", "Costs more than you have"), field validation, the failed-job title; none
        //    of it is a fill. At full strength the primary measures 2.92:1 against the card
        //    surface, short of the 4.5:1 WCAG asks of body text, and these are precisely the
        //    labels a player must read to learn why something was refused.
        //
        // So: same hue as primary, lifted far enough to read on both grounds it appears against.
        final theme = packTheme(packWith(primary: '#C81E2C'));
        final error = theme.colorScheme.error;
        final primary = theme.colorScheme.primary;

        expect(_hueDelta(error, primary), lessThan(6),
            reason: 'danger must stay the pack hue, not become a second red');
        expect(error.computeLuminance(), greaterThan(primary.computeLuminance()),
            reason: 'it is lifted off the primary, which is what buys the contrast');

        expect(_contrastRatio(error, theme.colorScheme.surface), greaterThanOrEqualTo(4.5),
            reason: 'gate and validation text must be readable on a card');
        expect(_contrastRatio(error, theme.scaffoldBackgroundColor), greaterThanOrEqualTo(4.5),
            reason: 'and on the page behind the cards');
      });

      test('display text roles use the Bebas Neue face', () {
        final theme = packTheme(packWith());

        expect(theme.textTheme.titleLarge?.fontFamily, 'Bebas Neue');
        expect(theme.textTheme.titleMedium?.fontFamily, 'Bebas Neue');
        expect(theme.textTheme.headlineSmall?.fontFamily, 'Bebas Neue');
      });
    });
  });
}

/// WCAG 2.x contrast ratio between two opaque colours.
///
/// `Color.computeLuminance` is already WCAG relative luminance, so this is just the ratio formula
/// around it: `(lighter + 0.05) / (darker + 0.05)`. 4.5:1 is the AA threshold for body text, 3:1
/// for large text and non-text UI.
double _contrastRatio(Color a, Color b) {
  final la = a.computeLuminance();
  final lb = b.computeLuminance();
  final lighter = la > lb ? la : lb;
  final darker = la > lb ? lb : la;
  return (lighter + 0.05) / (darker + 0.05);
}

/// Shortest angular distance between two colours' hues, in degrees.
///
/// Hue is a circle, so a naive subtraction makes 355° and 0° look 355° apart when they are 5°
/// apart — which matters here because the pack's red sits just below 360°.
double _hueDelta(Color a, Color b) {
  final raw = (HSLColor.fromColor(a).hue - HSLColor.fromColor(b).hue).abs() % 360;
  return raw > 180 ? 360 - raw : raw;
}
