import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/inventory_screen.dart';
import 'package:underworld_empire/screens/store_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/theme/pack_theme.dart';

import 'fakes.dart';

/// The app's own theme, built as a desktop browser builds it: Flutter picks a compact visual density
/// from the platform when the theme is made, and a phone-sized window on a desktop browser (or a
/// touch laptop) shrinks every button's hit area with it.
ThemeData _desktopTheme() {
  debugDefaultTargetPlatformOverride = TargetPlatform.windows;
  addTearDown(() => debugDefaultTargetPlatformOverride = null);
  final theme = packTheme(null);
  debugDefaultTargetPlatformOverride = null;
  return theme;
}

Future<void> _pump(WidgetTester tester, Widget Function(AppState) screen) async {
  tester.view.physicalSize = const Size(375, 667);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  await tester.pumpWidget(MaterialApp(theme: _desktopTheme(), home: screen(state)));
  await tester.pumpAndSettle();
}

void main() {
  group('touch targets under a desktop-platform theme', () {
    testWidgets('Buy and Equip on the Market can be hit within 44px', (tester) async {
      await _pump(tester, (s) => StoreScreen(appState: s));

      for (final key in ['buy-item-brass-knuckles', 'equip-item-brass-knuckles', 'buy-item-leather-jacket']) {
        final size = tester.getSize(find.byKey(Key(key)));
        expect(size.height, greaterThanOrEqualTo(44), reason: '$key is ${size.height}px tall');
      }
    });

    testWidgets('Equip and Unequip in the Inventory can be hit within 44px', (tester) async {
      await _pump(tester, (s) => InventoryScreen(appState: s));

      final size = tester.getSize(find.byKey(const Key('equip-item-brass-knuckles')));
      expect(size.height, greaterThanOrEqualTo(44), reason: 'equip is ${size.height}px tall');
    });
  });
}
