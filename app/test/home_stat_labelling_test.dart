import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// T4.2, narrowing A.5.1.
///
/// Home reads `me.attack` — the base stat — while Inventory shows the effective total including
/// equipped gear. A tester following the CP3 script equips a weapon, watches Inventory read 5,
/// looks at Home, sees 1, and reports a bug that is not one.
///
/// Where effective stats belong is a server-shape question (`/me` does not return a loadout, by
/// deliberate decision) and stays open. Labelling the figure costs nothing and settles the
/// misreading either way.
void main() {
  testWidgets('Home labels its combat stats as base figures', (tester) async {
    final state = AppState(
      apiClient: FakeApiClient(),
      tokenStore: InMemoryTokenStore(),
    );
    await state.bootstrap();

    await tester.pumpWidget(MaterialApp(home: HomeScreen(appState: state)));
    await tester.pump();

    final attack = tester.widget<Text>(find.byKey(const Key('home-attack')));
    final defense = tester.widget<Text>(find.byKey(const Key('home-defense')));

    expect(attack.data, startsWith('Base attack'));
    expect(defense.data, startsWith('Base defense'));
  });

  testWidgets('the base figure still shows the value from /me', (tester) async {
    final state = AppState(
      apiClient: FakeApiClient(),
      tokenStore: InMemoryTokenStore(),
    );
    await state.bootstrap();

    await tester.pumpWidget(MaterialApp(home: HomeScreen(appState: state)));
    await tester.pump();

    final attack = tester.widget<Text>(find.byKey(const Key('home-attack')));

    expect(attack.data, contains('${state.me!.attack}'));
  });
}
