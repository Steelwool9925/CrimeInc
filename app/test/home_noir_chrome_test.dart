import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/theme/pack_theme.dart';
import 'package:underworld_empire/widgets/halftone_wash.dart';
import 'package:underworld_empire/widgets/speedline_divider.dart';

import 'fakes.dart';

/// The comic-noir chrome added on top of the base theme: the halftone app-bar wash, the
/// speed-line divider before the dashboard grid, and the dimmed border on "coming soon" tiles
/// that keeps the theme's one spot colour out of a state that isn't an error.
void main() {
  Me meWith() => const Me(
        playerId: 'p',
        energy: MeterState(current: 7, max: 10, regenSecondsPerPoint: 300),
        stamina: MeterState(current: 5, max: 10, regenSecondsPerPoint: 300),
        health: MeterState(current: 10, max: 10, regenSecondsPerPoint: 300),
        cash: 12480,
        premiumCurrency: 0,
        level: 7,
        xp: 1240,
        skillPoints: 0,
        attack: 18,
        defense: 14,
      );

  Future<AppState> pumpHome(WidgetTester tester) async {
    final state = AppState(apiClient: FakeApiClient(meResult: meWith()), tokenStore: InMemoryTokenStore());
    await state.bootstrap();

    await tester.pumpWidget(MaterialApp(
      theme: packTheme(null),
      home: HomeScreen(appState: state, onChangeServer: () {}),
    ));
    await tester.pumpAndSettle();

    return state;
  }

  testWidgets('the app bar carries the halftone wash', (tester) async {
    await pumpHome(tester);

    expect(find.byType(HalftoneWash), findsOneWidget);
  });

  testWidgets('a speed-line divider marks the section break before the dashboard grid',
      (tester) async {
    await pumpHome(tester);

    expect(find.byType(SpeedlineDivider), findsOneWidget);
  });

  testWidgets('"coming soon" tiles get the dim border, not the theme\'s spot colour',
      (tester) async {
    await pumpHome(tester);

    final theme = packTheme(null);

    Color? borderColorOf(Key tileKey) {
      // The tile's own Key is on the Card itself (see _DashboardTile).
      final card = tester.widget<Card>(find.byKey(tileKey));
      final shape = card.shape as RoundedRectangleBorder?;
      return shape?.side.color;
    }

    final activeBorder = borderColorOf(const Key('go-to-jobs'));
    final soonBorder = borderColorOf(const Key('go-to-crew'));

    expect(soonBorder, isNot(activeBorder));
    // Whatever the dim tone is, it must not be the theme's one spot colour -- "not yet built"
    // is not a danger/CTA state.
    expect(soonBorder, isNot(theme.colorScheme.primary));
  });

  testWidgets('cash and level render in the tabular stat-number style', (tester) async {
    await pumpHome(tester);

    final cash = tester.widget<Text>(find.text('12480'));
    expect(cash.style?.fontFeatures, contains(const FontFeature.tabularFigures()));

    final level = tester.widget<Text>(find.byKey(const Key('home-level')));
    expect(level.style?.fontFeatures, contains(const FontFeature.tabularFigures()));
  });
}
