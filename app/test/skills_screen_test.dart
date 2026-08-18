import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/skills_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Me meWith({int skillPoints = 0, int attack = 10, int defense = 10}) => Me(
      playerId: 'p',
      energy: const MeterState(current: 7, max: 10, regenSecondsPerPoint: 300),
      stamina: const MeterState(current: 5, max: 10, regenSecondsPerPoint: 300),
      health: const MeterState(current: 10, max: 10, regenSecondsPerPoint: 300),
      cash: 100,
      premiumCurrency: 0,
      level: 2,
      xp: 100,
      skillPoints: skillPoints,
      attack: attack,
      defense: defense,
    );

Future<AppState> pumpSkills(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: SkillsScreen(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

void main() {
  group('SkillsScreen — display', () {
    testWidgets('shows unspent points and current stats', (tester) async {
      await pumpSkills(tester, FakeApiClient(meResult: meWith(skillPoints: 3, attack: 12, defense: 8)));

      expect(find.byKey(const Key('skill-points')), findsOneWidget);
      expect(find.text('3'), findsOneWidget);
      expect(find.text('12'), findsOneWidget);
      expect(find.text('8'), findsOneWidget);
    });

    testWidgets('shows both allocatable stats', (tester) async {
      await pumpSkills(tester, FakeApiClient(meResult: meWith(skillPoints: 1)));

      expect(find.byKey(const Key('stat-attack')), findsOneWidget);
      expect(find.byKey(const Key('stat-defense')), findsOneWidget);
    });
  });

  group('SkillsScreen — allocation buttons', () {
    testWidgets('buttons are enabled when points remain', (tester) async {
      await pumpSkills(tester, FakeApiClient(meResult: meWith(skillPoints: 1)));

      final attack = tester.widget<IconButton>(find.byKey(const Key('allocate-attack')));
      expect(attack.onPressed, isNotNull);
    });

    testWidgets('buttons are disabled with no points', (tester) async {
      await pumpSkills(tester, FakeApiClient(meResult: meWith(skillPoints: 0)));

      final attack = tester.widget<IconButton>(find.byKey(const Key('allocate-attack')));
      final defense = tester.widget<IconButton>(find.byKey(const Key('allocate-defense')));
      expect(attack.onPressed, isNull);
      expect(defense.onPressed, isNull);
    });
  });

  group('SkillsScreen — spending', () {
    testWidgets('tapping attack sends the attack target', (tester) async {
      final api = FakeApiClient(meResult: meWith(skillPoints: 1));
      await pumpSkills(tester, api);

      await tester.tap(find.byKey(const Key('allocate-attack')));
      await tester.pumpAndSettle();

      expect(api.allocateCalls, ['attack']);
    });

    testWidgets('tapping defense sends the defense target', (tester) async {
      final api = FakeApiClient(meResult: meWith(skillPoints: 1));
      await pumpSkills(tester, api);

      await tester.tap(find.byKey(const Key('allocate-defense')));
      await tester.pumpAndSettle();

      expect(api.allocateCalls, ['defense']);
    });

    testWidgets('the stat updates from the server response', (tester) async {
      // The new figure comes from the server, not from client-side arithmetic — the stat increase
      // per point is a balance value the client must not assume.
      final api = FakeApiClient(
        meResult: meWith(skillPoints: 1, attack: 10),
        allocateResult: meWith(skillPoints: 0, attack: 11),
      );
      await pumpSkills(tester, api);

      await tester.tap(find.byKey(const Key('allocate-attack')));
      await tester.pumpAndSettle();

      expect(find.text('11'), findsOneWidget);
      expect(find.text('0'), findsOneWidget); // points consumed
    });

    testWidgets('a refusal shows a readable message', (tester) async {
      final api = FakeApiClient(
        meResult: meWith(skillPoints: 1),
        refuseAllocateWith: 'noSkillPoints',
      );
      await pumpSkills(tester, api);

      await tester.tap(find.byKey(const Key('allocate-attack')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('skills-message')), findsOneWidget);
      expect(find.text('No skill points to spend.'), findsOneWidget);
    });
  });
}
