import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> bootstrapped(FakeApiClient api, {DateTime Function()? clock}) async {
  final state = clock == null
      ? AppState(apiClient: api, tokenStore: InMemoryTokenStore())
      : AppState(apiClient: api, tokenStore: InMemoryTokenStore(), clock: clock);
  await state.bootstrap();
  return state;
}

/// Lets the fire-and-forget reads a bootstrap or action starts run to completion.
Future<void> settle() => Future<void>.delayed(Duration.zero);

void main() {
  group('AppState.loadObjectives()', () {
    test('is read once signing in, so Home can say how many are outstanding', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();

      expect(api.objectivesCalls, 1);
      expect(state.objectivesLoaded, isTrue);
      expect(state.objectives.map((o) => o.id), ['corner-store-regular', 'two-different-jobs', 'busy-day']);
    });

    test('keeps the reset instant the server sent', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();

      expect(state.objectivesResetsAt, DateTime.utc(2026, 10, 14));
    });

    test('records an error when the read fails, and says nothing was loaded', () async {
      final state = await bootstrapped(FakeApiClient(throwOnObjectives: true));
      await settle();

      expect(state.objectivesLoaded, isFalse);
      expect(state.objectivesError, isNotNull);
      expect(state.objectives, isEmpty);
    });

    test('does nothing before sign-in (no token)', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.loadObjectives();

      expect(api.objectivesCalls, 0);
    });

    test('collapses a read started while another is in flight', () async {
      final api = FakeApiClient()..objectivesGate = Completer<void>();
      final state = await bootstrapped(api);
      await settle();
      expect(api.objectivesCalls, 1, reason: 'the sign-in read is parked on the gate');

      final second = state.loadObjectives();
      await second;

      expect(api.objectivesCalls, 1);
      api.objectivesGate!.complete();
      await settle();
    });
  });

  group('counts for the Home tile', () {
    test('count what is ready to claim, and what is left', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();

      // open (2/5), done-unclaimed, claimed
      expect(state.objectivesToClaim, 1);
      expect(state.objectivesOutstanding, 2);
    });
  });

  group('objectives folded from an action response', () {
    test('a won job replaces the list with the one its response carried', () async {
      final moved = DailyObjective(
        id: kObjectiveOpen.id,
        name: kObjectiveOpen.name,
        description: kObjectiveOpen.description,
        type: kObjectiveOpen.type,
        target: 5,
        jobId: kObjectiveOpen.jobId,
        progress: 3,
        complete: false,
        claimed: false,
        rewardCash: 110,
        rewardXp: 25,
      );
      final api = FakeApiClient(
        doJobResult: DoJobResult(
          result: kSucceededJobResult,
          player: kFakeMe,
          objectives: [moved],
        ),
      );
      final state = await bootstrapped(api);
      await settle();
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.objectives.single.progress, 3);
      expect(api.objectivesCalls, 1, reason: 'no extra round trip: the response carried them');
    });

    test('a response from an older server, with none, leaves the list alone', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.objectives, hasLength(3));
    });

    test('a property collection replaces the list too', () async {
      final api = FakeApiClient(
        collectPropertyResult: CollectPropertyResult(
          propertyId: 'street-corner',
          cashCollected: 30,
          pending: 0,
          cap: 60,
          player: kPropertyCollectedMe,
          objectives: const [kObjectiveProperty],
        ),
      );
      final state = await bootstrapped(api);
      await settle();
      await state.loadProperties();

      await state.collectProperty('street-corner');

      expect(state.objectives.single.id, 'collect-rent');
    });

    test('an action response retires a read that was still in flight', () async {
      final api = FakeApiClient(
        doJobResult: DoJobResult(
          result: kSucceededJobResult,
          player: kFakeMe,
          objectives: const [kObjectiveProperty],
        ),
      );
      final state = await bootstrapped(api);
      await settle();
      await state.loadJobs();

      // A stale read is parked; the action lands first; then the stale read finishes.
      api.objectivesGate = Completer<void>();
      final stale = state.loadObjectives();
      await state.doJob('shake-down-corner-store');
      api.objectivesGate!.complete();
      await stale;

      expect(state.objectives.single.id, 'collect-rent', reason: 'the older read must not win');
      expect(state.objectivesLoading, isFalse);
    });
  });

  group('AppState.claimObjective()', () {
    test('pays the reward: the player is replaced and the objective reads as claimed', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();
      final before = state.me!.cash;

      await state.claimObjective('two-different-jobs');

      expect(api.claimObjectiveCalls, ['two-different-jobs']);
      expect(state.me!.cash, before + 60);
      expect(state.objectives.firstWhere((o) => o.id == 'two-different-jobs').claimed, isTrue);
      expect(state.objectivesToClaim, 0);
      expect(state.objectiveInFlight, isNull);
    });

    test('a second claim while one is on its way is ignored', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();

      final first = state.claimObjective('two-different-jobs');
      final second = state.claimObjective('two-different-jobs');
      await Future.wait([first, second]);

      expect(api.claimObjectiveCalls, hasLength(1));
    });

    test('a level-up from the reward is queued for the dialog', () async {
      final api = FakeApiClient(
        claimObjectiveResult: ClaimObjectiveResult(
          claimed: kObjectiveClaimed,
          player: kObjectiveClaimedMe,
          levelUp: const LevelUp(
            levelsGained: 1,
            newLevel: 3,
            skillPointsGranted: 1,
            metersRefilled: true,
          ),
          objectives: const [kObjectiveClaimed],
        ),
      );
      final state = await bootstrapped(api);
      await settle();

      await state.claimObjective('two-different-jobs');

      expect(state.pendingLevelUp?.newLevel, 3);
    });

    test('a refusal is worded for the player, scoped to objectives, and re-reads the list', () async {
      final api = FakeApiClient(refuseClaimWith: 'alreadyClaimed');
      final state = await bootstrapped(api);
      await settle();
      final reads = api.objectivesCalls;

      await state.claimObjective('two-different-jobs');
      await settle();

      expect(state.actionMessageFor(ActionScope.objectives), 'You already took that reward.');
      expect(state.actionMessageFor(ActionScope.jobs), isNull);
      expect(api.objectivesCalls, reads + 1, reason: 'the screen disagreed with the server');
      expect(state.objectiveInFlight, isNull);
    });

    test('each refusal token has its own wording', () async {
      for (final (reason, text) in [
        ('notComplete', 'That objective is not finished yet.'),
        ('notToday', 'That objective is not one of yours today.'),
      ]) {
        final state = await bootstrapped(FakeApiClient(refuseClaimWith: reason));
        await settle();

        await state.claimObjective('two-different-jobs');

        expect(state.actionMessageFor(ActionScope.objectives), text);
      }
    });
  });

  group('a new UTC day', () {
    test('a refresh after the reset instant re-reads the set', () async {
      var now = DateTime.utc(2026, 10, 13, 23, 59);
      final api = FakeApiClient();
      final state = await bootstrapped(api, clock: () => now);
      await settle();
      final reads = api.objectivesCalls;

      await state.refresh();
      await settle();
      expect(api.objectivesCalls, reads, reason: 'still the same day');

      now = DateTime.utc(2026, 10, 14, 0, 0, 1);
      await state.refresh();
      await settle();

      expect(api.objectivesCalls, reads + 1);
    });
  });
}
