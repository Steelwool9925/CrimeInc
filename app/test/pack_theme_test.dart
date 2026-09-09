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

      test('error/danger colour is the same red as primary -- one spot colour, not two reds', () {
        // The theme's whole point is colour discipline: a second, independently-generated
        // "error red" from ColorScheme.fromSeed would quietly break that.
        final theme = packTheme(packWith(primary: '#C81E2C'));

        expect(theme.colorScheme.error, theme.colorScheme.primary);
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
