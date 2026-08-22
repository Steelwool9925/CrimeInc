import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Me meWith({int level = 1, int xp = 0, int skillPoints = 0, int attack = 10, int defense = 10}) => Me(
      playerId: 'p',
      energy: const MeterState(current: 7, max: 10, regenSecondsPerPoint: 300),
      stamina: const MeterState(current: 5, max: 10, regenSecondsPerPoint: 300),
      health: const MeterState(current: 10, max: 10, regenSecondsPerPoint: 300),
      cash: 100,
      premiumCurrency: 0,
      level: level,
      xp: xp,
      skillPoints: skillPoints,
      attack: attack,
      defense: defense,
    );

Future<AppState> pumpHome(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: HomeScreen(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

void main() {
  group('HomeScreen — progression card', () {
    testWidgets('shows level and xp', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(level: 3, xp: 450)));

      expect(find.byKey(const Key('home-level')), findsOneWidget);
      expect(find.text('3'), findsOneWidget);
      expect(find.text('450 XP'), findsOneWidget);
    });

    testWidgets('shows attack and defense — CP2 checks the stat here', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(attack: 11, defense: 7)));

      // Labelled "Base" since T4.2: these exclude equipped gear, unlike Inventory's totals.
      expect(find.text('Base attack 11'), findsOneWidget);
      expect(find.text('Base defense 7'), findsOneWidget);
    });

    testWidgets('prompts when skill points are unspent', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 1)));

      expect(find.byKey(const Key('home-skill-points')), findsOneWidget);
      expect(find.text('1 skill point to spend'), findsOneWidget);
    });

    testWidgets('pluralises the skill point prompt', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 3)));

      expect(find.text('3 skill points to spend'), findsOneWidget);
    });

    testWidgets('hides the prompt with no points', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 0)));

      expect(find.byKey(const Key('home-skill-points')), findsNothing);
    });
  });

  group('HomeScreen — navigation', () {
    testWidgets('offers a route to Jobs', (tester) async {
      await pumpHome(tester, FakeApiClient());

      expect(find.byKey(const Key('go-to-jobs')), findsOneWidget);
    });

    testWidgets('tapping Jobs opens the jobs screen', (tester) async {
      await pumpHome(tester, FakeApiClient());

      // The nav buttons sit below the 800x600 test viewport inside Home's scroll view, so they must
      // be scrolled into view before a tap can land on them.
      await tester.ensureVisible(find.byKey(const Key('go-to-jobs')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-jobs')));
      await tester.pumpAndSettle();
      // JobsScreen loads its catalog in a post-frame callback; let that async load land.
      await tester.pumpAndSettle();

      expect(find.text('Jobs'), findsWidgets);
      expect(find.byKey(const Key('job-card-shake-down-corner-store')), findsOneWidget);
    });

    testWidgets('tapping Skills opens the skills screen', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 1)));

      await tester.ensureVisible(find.byKey(const Key('go-to-skills')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-skills')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('skill-points')), findsOneWidget);
    });

    testWidgets('the Skills button shows the unspent count', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 2)));

      expect(find.text('Skills (2)'), findsOneWidget);
    });

    testWidgets('the Skills button omits the count at zero', (tester) async {
      await pumpHome(tester, FakeApiClient(meResult: meWith(skillPoints: 0)));

      expect(find.text('Skills'), findsOneWidget);
    });
  });
}
