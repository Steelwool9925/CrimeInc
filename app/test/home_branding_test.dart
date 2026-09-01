import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/widgets/pack_logo.dart';

import 'fakes.dart';

/// T4.1: Home shows the pack's own branding, fetched by logical key.
///
/// These assert the widget tree and the URL rather than rendered pixels — the test binding
/// serves no network images, and what matters is that the app asks the manifest-driven route
/// for its art instead of shipping a bundled asset that could not be swapped.
void main() {
  AppState stateWith(ContentPack pack) => AppState(
        apiClient: FakeApiClient(contentPackResult: pack),
        tokenStore: InMemoryTokenStore(),
      );

  ContentPack packWith(Map<String, String> assets) => ContentPack(
        name: 'Underworld Empire',
        cashLabel: 'Cash',
        premiumLabel: 'Gold Bars',
        primaryColorHex: '#B71C1C',
        assets: assets,
      );

  Widget wrap(AppState state) => MaterialApp(home: HomeScreen(appState: state));

  testWidgets('requests the logo from the manifest-driven asset route', (tester) async {
    final state = stateWith(packWith(const {'logo': 'assets/logo.png'}));
    await state.bootstrap();

    await tester.pumpWidget(wrap(state));
    await tester.pump();

    final logo = tester.widget<Image>(find.byKey(PackLogo.imageKey));
    expect((logo.image as NetworkImage).url, endsWith('/content/assets/logo'));
  });

  testWidgets('falls back to the pack name when the manifest ships no logo', (tester) async {
    // A pack is not obliged to supply art; the app must not request a URL that will 404.
    final state = stateWith(packWith(const {}));
    await state.bootstrap();

    await tester.pumpWidget(wrap(state));
    await tester.pump();

    expect(find.byKey(PackLogo.imageKey), findsNothing);
    expect(find.text('Underworld Empire'), findsWidgets);
  });

  testWidgets('a logo declared but left blank is treated as absent', (tester) async {
    final state = stateWith(packWith(const {'logo': ''}));
    await state.bootstrap();

    await tester.pumpWidget(wrap(state));
    await tester.pump();

    expect(find.byKey(PackLogo.imageKey), findsNothing);
  });

  testWidgets(
      'the app bar sets no colour of its own, so it inherits the Steamnoir AppBarTheme',
      (tester) async {
    // Colouring the app bar from the pack is now packTheme's job (see pack_theme.dart /
    // pack_theme_test.dart), applied once at the MaterialApp level, not something HomeScreen
    // re-derives and overrides per screen. This test's own MaterialApp wrapper carries no
    // packTheme, so an unset backgroundColor here is the signal HomeScreen left theming alone.
    final state = stateWith(packWith(const {'logo': 'assets/logo.png'}));
    await state.bootstrap();

    await tester.pumpWidget(wrap(state));
    await tester.pump();

    final appBar = tester.widget<AppBar>(find.byType(AppBar));
    expect(appBar.backgroundColor, isNull);
  });
}
