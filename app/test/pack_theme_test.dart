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
    });

    test('is not the old hardcoded deepPurple seed', () {
      // Guards the regression this task exists to fix.
      final theme = packTheme(packWith(primary: '#B71C1C'));
      final oldDefault = ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple),
      );

      expect(theme.colorScheme.primary, isNot(oldDefault.colorScheme.primary));
    });
  });
}
