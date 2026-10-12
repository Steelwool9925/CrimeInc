import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
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

Future<AppState> _bootstrapped(FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  await pumpEventQueue();
  return state;
}

void main() {
  group('a message belongs to the area that raised it', () {
    void expectOnlyIn(AppState state, ActionScope scope, String text) {
      for (final s in ActionScope.values) {
        expect(state.actionMessageFor(s), s == scope ? text : isNull, reason: 'scope $s');
      }
    }

    test('a skills refusal is not shown to jobs, the market or properties', () async {
      final state = await _bootstrapped(
        FakeApiClient(meResult: _withPoints(1), refuseAllocateWith: 'noSkillPoints'),
      );

      await state.allocateSkill('attack');

      expectOnlyIn(state, ActionScope.skills, 'No skill points to spend.');
      expect(state.actionMessage, 'No skill points to spend.', reason: 'the raw message is unchanged');
    });

    test('a job refusal stays with jobs', () async {
      final state = await _bootstrapped(FakeApiClient(refuseDoJobWith: 'energy'));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expectOnlyIn(state, ActionScope.jobs, 'Not enough energy.');
    });

    test('an item refusal stays with the market and inventory', () async {
      final state = await _bootstrapped(FakeApiClient(refuseItemActionWith: 'cash'));

      await state.buyItem('brass-knuckles');

      expectOnlyIn(state, ActionScope.items, 'You cannot afford that.');
    });

    test('a property refusal stays with properties', () async {
      final state = await _bootstrapped(FakeApiClient(refusePropertyActionWith: 'cash'));
      await state.loadProperties();

      await state.buyProperty('street-corner');

      expectOnlyIn(state, ActionScope.properties, 'You cannot afford that.');
    });

    test('a newer message in another area replaces it, and clearing removes it everywhere', () async {
      final state = await _bootstrapped(
        FakeApiClient(meResult: _withPoints(1), refuseAllocateWith: 'noSkillPoints', refuseItemActionWith: 'cash'),
      );
      await state.allocateSkill('attack');
      await state.buyItem('brass-knuckles');

      expect(state.actionMessageFor(ActionScope.skills), isNull);
      expect(state.actionMessageFor(ActionScope.items), 'You cannot afford that.');

      state.clearActionMessage();

      for (final s in ActionScope.values) {
        expect(state.actionMessageFor(s), isNull);
      }
    });
  });

  group('spending a skill point', () {
    test('a second tap while the first is in flight is ignored, so a double-click spends once', () async {
      final gate = Completer<void>();
      final api = FakeApiClient(meResult: _withPoints(1), allocateResult: _withPoints(0))..allocateGate = gate;
      final state = await _bootstrapped(api);

      final first = state.allocateSkill('attack');
      await pumpEventQueue();
      expect(state.skillInFlight, isTrue);

      await state.allocateSkill('attack');
      expect(api.allocateCalls, ['attack'], reason: 'the second tap must not reach the server');

      gate.complete();
      await first;

      expect(state.skillInFlight, isFalse);
      expect(state.me!.skillPoints, 0);
      expect(state.actionMessage, isNull, reason: 'no conflict banner for a spend that worked');
    });

    test('the guard lifts after a refusal so the player can try again', () async {
      final api = FakeApiClient(meResult: _withPoints(1), refuseAllocateWith: 'noSkillPoints');
      final state = await _bootstrapped(api);

      await state.allocateSkill('attack');
      await state.allocateSkill('defense');

      expect(api.allocateCalls, ['attack', 'defense']);
      expect(state.skillInFlight, isFalse);
    });
  });
}
