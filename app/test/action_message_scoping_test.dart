import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/inventory_screen.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/screens/properties_screen.dart';
import 'package:underworld_empire/screens/skills_screen.dart';
import 'package:underworld_empire/screens/store_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Me _withPoints(int points) => Me(
      playerId: kFakeMe.playerId,
      energy: kFakeMe.energy,
      stamina: kFakeMe.stamina,
      health: kFakeMe.health,
      cash: kFakeMe.cash,
      premiumCurrency: 0,
      level: kFakeMe.level,
      xp: 0,
      skillPoints: points,
      attack: kFakeMe.attack,
      defense: kFakeMe.defense,
    );

/// A player who has just been told "No skill points to spend." on the Skills screen and has then
/// opened something else: the message must not follow them.
Future<AppState> _stateWithSkillsMessage({Completer<void>? gate}) async {
  final api = FakeApiClient(meResult: _withPoints(1), refuseAllocateWith: 'noSkillPoints');
  if (gate != null) api.allocateGate = gate;
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  if (gate == null) await state.allocateSkill('attack');
  return state;
}

Future<void> _show(WidgetTester tester, Widget screen) async {
  await tester.pumpWidget(MaterialApp(home: screen));
  await tester.pumpAndSettle();
}

void main() {
  group('a skills message does not follow the player to other screens', () {
    testWidgets('not the Market', (tester) async {
      final state = await _stateWithSkillsMessage();
      expect(state.actionMessage, 'No skill points to spend.');

      await _show(tester, StoreScreen(appState: state));

      expect(find.byKey(const Key('action-message')), findsNothing);
      expect(find.text('No skill points to spend.'), findsNothing);
    });

    testWidgets('not the Inventory', (tester) async {
      final state = await _stateWithSkillsMessage();

      await _show(tester, InventoryScreen(appState: state));

      expect(find.byKey(const Key('action-message')), findsNothing);
    });

    testWidgets('not Jobs', (tester) async {
      final state = await _stateWithSkillsMessage();

      await _show(tester, JobsScreen(appState: state));

      expect(find.byKey(const Key('action-message')), findsNothing);
    });

    testWidgets('not Properties', (tester) async {
      final state = await _stateWithSkillsMessage();

      await _show(tester, PropertiesScreen(appState: state));

      expect(find.byKey(const Key('action-message')), findsNothing);
    });

    testWidgets('but the Skills screen still shows it', (tester) async {
      final state = await _stateWithSkillsMessage();

      await _show(tester, SkillsScreen(appState: state));

      expect(find.byKey(const Key('skills-message')), findsOneWidget);
      expect(find.text('No skill points to spend.'), findsOneWidget);
    });
  });

  group('a market refusal still shows on the Market and Inventory', () {
    testWidgets('and on neither Skills nor Jobs', (tester) async {
      final api = FakeApiClient(refuseItemActionWith: 'cash');
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
          await state.buyItem('brass-knuckles');

      await _show(tester, StoreScreen(appState: state));
      expect(find.text('You cannot afford that.'), findsOneWidget);

      await _show(tester, InventoryScreen(appState: state));
      expect(find.text('You cannot afford that.'), findsOneWidget);

      await _show(tester, SkillsScreen(appState: state));
      expect(find.byKey(const Key('skills-message')), findsNothing);

      await _show(tester, JobsScreen(appState: state));
      expect(find.byKey(const Key('action-message')), findsNothing);
    });
  });

  group('SkillsScreen while a point is being spent', () {
    testWidgets('both buttons are disabled until the answer arrives', (tester) async {
      final gate = Completer<void>();
      final api = FakeApiClient(meResult: _withPoints(2), allocateResult: _withPoints(1))..allocateGate = gate;
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await _show(tester, SkillsScreen(appState: state));

      await tester.tap(find.byKey(const Key('allocate-attack')));
      await tester.pump();

      expect(tester.widget<IconButton>(find.byKey(const Key('allocate-attack'))).onPressed, isNull);
      expect(tester.widget<IconButton>(find.byKey(const Key('allocate-defense'))).onPressed, isNull);

      gate.complete();
      await tester.pumpAndSettle();

      expect(tester.widget<IconButton>(find.byKey(const Key('allocate-attack'))).onPressed, isNotNull);
      expect(api.allocateCalls, ['attack']);
    });

    testWidgets('a double-click spends once and shows no message', (tester) async {
      final gate = Completer<void>();
      final api = FakeApiClient(meResult: _withPoints(2), allocateResult: _withPoints(1))..allocateGate = gate;
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await _show(tester, SkillsScreen(appState: state));

      await tester.tap(find.byKey(const Key('allocate-attack')));
      await tester.tap(find.byKey(const Key('allocate-attack')), warnIfMissed: false);
      gate.complete();
      await tester.pumpAndSettle();

      expect(api.allocateCalls, ['attack']);
      expect(find.byKey(const Key('skills-message')), findsNothing);
    });
  });
}
