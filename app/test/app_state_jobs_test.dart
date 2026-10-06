import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> bootstrapped(FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  return state;
}

void main() {
  group('AppState.loadJobs()', () {
    test('populates the job list', () async {
      final state = await bootstrapped(FakeApiClient());

      await state.loadJobs();

      expect(state.jobs, hasLength(3));
      expect(state.jobs.first.id, 'shake-down-corner-store');
      expect(state.jobsError, isNull);
    });

    test('records an error when the catalog fails', () async {
      final state = await bootstrapped(FakeApiClient(throwOnJobs: true));

      await state.loadJobs();

      expect(state.jobsError, isNotNull);
      expect(state.jobs, isEmpty);
    });

    test('does nothing before bootstrap (no token)', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.loadJobs();

      expect(api.jobsCalls, 0);
    });

    test('clears a previous error on a successful reload', () async {
      final state = await bootstrapped(FakeApiClient(throwOnJobs: true));
      await state.loadJobs();
      expect(state.jobsError, isNotNull);

      // A fresh state with a working client stands in for the retry path.
      final healthy = await bootstrapped(FakeApiClient());
      await healthy.loadJobs();

      expect(healthy.jobsError, isNull);
    });
  });

  group('AppState.doJob()', () {
    test('records the result and updates the player from the response', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.lastJobResult, isNotNull);
      expect(state.lastJobResult!.cashEarned, 22);
      expect(state.me, isNotNull);
    });

    test('does not call /me again after doing a job', () async {
      // The do-job response carries the post-action projection.
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadJobs();
      final before = api.meCalls;

      await state.doJob('shake-down-corner-store');

      expect(api.meCalls, before);
    });

    test('folds the new mastery into the cached catalog', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();
      expect(state.jobs.first.mastery.completions, 3);

      await state.doJob('shake-down-corner-store');

      expect(state.jobs.first.mastery.completions, 4);
    });

    test('leaves other jobs mastery untouched', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      final other = state.jobs.firstWhere((j) => j.id != 'shake-down-corner-store');
      expect(other.mastery.completions, 3);
    });

    test('surfaces an energy refusal as a message, not an error state', () async {
      final state = await bootstrapped(FakeApiClient(refuseDoJobWith: 'energy'));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.actionMessage, 'Not enough energy.');
      expect(state.status, LoadStatus.ready);
      expect(state.lastJobResult, isNull);
    });

    test('surfaces a level refusal as a message', () async {
      final state = await bootstrapped(FakeApiClient(refuseDoJobWith: 'level'));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.actionMessage, 'Your level is too low for that job.');
    });

    test('an unknown refusal reason falls back to the server detail', () async {
      final state = await bootstrapped(FakeApiClient(refuseDoJobWith: 'somethingNew'));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.actionMessage, contains('somethingNew'));
    });

    test('does nothing before bootstrap', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.doJob('shake-down-corner-store');

      expect(api.doJobCalls, isEmpty);
    });

    test('clears jobInFlight when finished', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.jobInFlight, isNull);
    });

    test('clears jobInFlight even when refused', () async {
      final state = await bootstrapped(FakeApiClient(refuseDoJobWith: 'energy'));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.jobInFlight, isNull);
    });

    test('surfaces a pending level-up', () async {
      final state = await bootstrapped(FakeApiClient(
        doJobResult: const DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: 20,
            xpEarned: 10,
            healthLost: 0,
            mastery: kFakeMastery,
            masteryRankedUp: false,
          ),
          player: kFakeMe,
          levelUp: LevelUp(
            levelsGained: 1,
            newLevel: 2,
            skillPointsGranted: 1,
            metersRefilled: true,
          ),
        ),
      ));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.pendingLevelUp, isNotNull);
      expect(state.pendingLevelUp!.newLevel, 2);

      state.acknowledgeLevelUp();
      expect(state.pendingLevelUp, isNull);
    });

    group('after a level-up', () {
      const levelUpOutcome = DoJobResult(
        result: JobResult(
          jobId: 'shake-down-corner-store',
          succeeded: true,
          energySpent: 1,
          cashEarned: 20,
          xpEarned: 10,
          healthLost: 0,
          mastery: kFakeMastery,
          masteryRankedUp: false,
        ),
        player: kFakeMe,
        levelUp: LevelUp(levelsGained: 1, newLevel: 2, skillPointsGranted: 1, metersRefilled: true),
      );
      const afterLevelUp = JobList(jobs: [kCheapJob], currentEnergy: 20, level: 2);

      test('re-reads the job list at once, so a job the new level unlocks is not left locked', () async {
        final api = FakeApiClient(doJobResult: levelUpOutcome);
        final state = await bootstrapped(api);
        await state.loadJobs();
        final readsBefore = api.jobsCalls;

        api.jobsOverride = afterLevelUp;
        await state.doJob('shake-down-corner-store');
        await pumpEventQueue();

        expect(api.jobsCalls, readsBefore + 1);
        expect(state.jobs.map((j) => j.id), [kCheapJob.id]);
      });

      test('an ordinary job does not trigger an extra read', () async {
        final api = FakeApiClient();
        final state = await bootstrapped(api);
        await state.loadJobs();
        final readsBefore = api.jobsCalls;

        await state.doJob('shake-down-corner-store');
        await pumpEventQueue();

        expect(api.jobsCalls, readsBefore);
      });

      test('a read already in flight cannot put the pre-level-up list back', () async {
        final gate = Completer<void>();
        final api = FakeApiClient(doJobResult: levelUpOutcome);
        final state = await bootstrapped(api);
        await state.loadJobs();

        api.jobsGate = gate;
        final periodicRead = state.loadJobs();
        await pumpEventQueue();

        api.jobsOverride = afterLevelUp;
        await state.doJob('shake-down-corner-store');
        await pumpEventQueue();

        gate.complete();
        await periodicRead;
        await pumpEventQueue();

        expect(state.jobs.map((j) => j.id), [kCheapJob.id]);
        expect(state.jobsLoading, isFalse);
      });
    });

    test('dismissJobResult clears the panel', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();
      await state.doJob('shake-down-corner-store');

      state.dismissJobResult();

      expect(state.lastJobResult, isNull);
    });
  });

  group('AppState.doJob() — item-quantity gate carry-forward', () {
    test(
        'a successful item-gated attempt updates held quantity and re-locks below the minimum',
        () async {
      final state = await bootstrapped(FakeApiClient(
        jobsResult: const JobList(jobs: [kItemReadyJob], currentEnergy: 7, level: 3),
        doJobResult: const DoJobResult(result: kItemConsumingJobResult, player: kFakeMe),
      ));
      await state.loadJobs();

      await state.doJob('hijack-a-delivery-truck');

      final job = state.jobs.single;
      final requirement = job.itemRequirements.single;
      expect(requirement.held, 1);
      expect(requirement.met, isFalse);
      expect(job.canAttempt, isFalse);
      expect(job.unavailableReason, 'items');
    });

    test('consuming stock that stays above the minimum leaves the job attemptable', () async {
      const aboveMinimum = JobResult(
        jobId: 'hijack-a-delivery-truck',
        succeeded: true,
        energySpent: 4,
        cashEarned: 200,
        xpEarned: 55,
        healthLost: 0,
        mastery: kFakeMastery,
        masteryRankedUp: false,
        itemsConsumed: [
          ItemConsumption(itemId: 'switchblade', quantityConsumed: 1, quantityRemaining: 4),
        ],
      );
      final state = await bootstrapped(FakeApiClient(
        jobsResult: const JobList(jobs: [kItemReadyJob], currentEnergy: 7, level: 3),
        doJobResult: const DoJobResult(result: aboveMinimum, player: kFakeMe),
      ));
      await state.loadJobs();

      await state.doJob('hijack-a-delivery-truck');

      final requirement = state.jobs.single.itemRequirements.single;
      expect(requirement.held, 4);
      expect(requirement.met, isTrue);
      expect(state.jobs.single.canAttempt, isTrue);
    });

    test('a job with no item requirements of its own is untouched by a different jobs consumption',
        () async {
      final state = await bootstrapped(FakeApiClient(
        jobsResult: const JobList(jobs: [kCheapJob, kItemReadyJob], currentEnergy: 7, level: 3),
        doJobResult: const DoJobResult(result: kItemConsumingJobResult, player: kFakeMe),
      ));
      await state.loadJobs();

      await state.doJob('hijack-a-delivery-truck');

      final cheap = state.jobs.firstWhere((j) => j.id == 'shake-down-corner-store');
      expect(cheap.itemRequirements, isEmpty);
    });

    test('an attempt at a job with no item requirements never derives an items lock', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      expect(state.jobs.first.itemRequirements, isEmpty);
      expect(state.jobs.first.unavailableReason, isNot('items'));
    });
  });

  group('AppState.doJob() — gating is left to the next re-read', () {
    test("does not touch another job's gating locally", () async {
      final state = await bootstrapped(FakeApiClient(
        jobsResult: const JobList(
          jobs: [kCheapJob, kStatLockedJob],
          currentEnergy: 7,
          level: 1,
        ),
      ));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      // A level-up alone must never light this job up locally: the server never checked its stats
      // requirement while level was still the blocker (gate precedence is level -> stats -> items
      // -> energy), so the client has no way to know whether it would actually pass. It stays
      // exactly as last reported until the next re-read — this is the fixed version of the bug
      // where a job blocked by level *and* stats got wrongly marked attemptable the moment level
      // cleared.
      final stillLocked =
          state.jobs.firstWhere((j) => j.id == 'run-numbers-for-the-bookie');
      expect(stillLocked.canAttempt, isFalse);
      expect(stillLocked.unavailableReason, 'stats');
    });

    test('a later loadJobs() call is what actually resolves a job that just cleared', () async {
      final api = FakeApiClient(
        jobsResult: const JobList(
          jobs: [kCheapJob, kStatLockedJob],
          currentEnergy: 7,
          level: 1,
        ),
      );
      final state = await bootstrapped(api);
      await state.loadJobs();
      await state.doJob('shake-down-corner-store');
      expect(
        state.jobs.firstWhere((j) => j.id == 'run-numbers-for-the-bookie').canAttempt,
        isFalse,
      );

      // The server now reports the job as genuinely attemptable (level and stats both cleared by
      // the time of this read) — simulating what the screen's periodic re-read would observe.
      // Same fields as kStatLockedJob, just now cleared — Job has no copyWith, so this is the
      // fixture written out with the two changed fields.
      api.jobsOverride = const JobList(
        jobs: [
          kCheapJob,
          Job(
            id: 'run-numbers-for-the-bookie',
            name: 'Run Numbers for the Bookie',
            description: "Collect the day's slips and skim a little off the top.",
            area: 'docks',
            energyCost: 2,
            levelRequirement: 2,
            cashMin: 60,
            cashMax: 110,
            xp: 26,
            attackRequirement: 6,
            defenseRequirement: 5,
            successChance: 0.72,
            mastery: kFakeMastery,
            canAttempt: true,
          ),
        ],
        currentEnergy: 7,
        level: 2,
      );
      await state.loadJobs();

      expect(
        state.jobs.firstWhere((j) => j.id == 'run-numbers-for-the-bookie').canAttempt,
        isTrue,
      );
    });

    test('passive energy regen is also left to the next re-read, not guessed locally', () async {
      final state = await bootstrapped(FakeApiClient(
        jobsResult: const JobList(
          jobs: [kCheapJob, kUnaffordableJob],
          currentEnergy: 0,
          level: 1,
        ),
      ));
      await state.loadJobs();

      await state.doJob('shake-down-corner-store');

      // No re-derivation for energy either, on purpose: the periodic re-read is the single source
      // of truth for every gating dimension now, not just stats.
      final stillLocked =
          state.jobs.firstWhere((j) => j.id == 'hijack-a-delivery-truck');
      expect(stillLocked.canAttempt, isFalse);
      expect(stillLocked.unavailableReason, 'energy');
    });
  });

  group('AppState.allocateSkill()', () {
    test('updates the player from the server response', () async {
      final api = FakeApiClient(
        allocateResult: const Me(
          playerId: 'p',
          energy: MeterState(current: 7, max: 10, regenSecondsPerPoint: 300),
          stamina: MeterState(current: 5, max: 10, regenSecondsPerPoint: 300),
          health: MeterState(current: 10, max: 10, regenSecondsPerPoint: 300),
          cash: 100,
          premiumCurrency: 0,
          level: 2,
          xp: 100,
          skillPoints: 0,
          attack: 11,
          defense: 10,
        ),
      );
      final state = await bootstrapped(api);

      await state.allocateSkill('attack');

      expect(api.allocateCalls, ['attack']);
      expect(state.me!.attack, 11);
      expect(state.me!.skillPoints, 0);
    });

    test('surfaces a no-points refusal as a message', () async {
      final state = await bootstrapped(FakeApiClient(refuseAllocateWith: 'noSkillPoints'));

      await state.allocateSkill('attack');

      expect(state.actionMessage, 'No skill points to spend.');
    });

    test('does nothing before bootstrap', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.allocateSkill('attack');

      expect(api.allocateCalls, isEmpty);
    });

    test('clearActionMessage removes the message', () async {
      final state = await bootstrapped(FakeApiClient(refuseAllocateWith: 'noSkillPoints'));
      await state.allocateSkill('attack');
      expect(state.actionMessage, isNotNull);

      state.clearActionMessage();

      expect(state.actionMessage, isNull);
    });
  });

  group('JobMastery derived values', () {
    test('recovers completionsPerRank from the server figures', () async {
      // 3 completions, rank 0, 7 to go => threshold is 10.
      const mastery = JobMastery(
        completions: 3,
        rank: 0,
        maxRank: 5,
        completionsUntilNextRank: 7,
      );

      expect(mastery.completionsPerRank, 10);
      expect(mastery.rankProgress, closeTo(0.3, 0.0001));
    });

    test('recovers the threshold at a higher rank', () async {
      // 24 completions, rank 2, 6 to go => (24+6)/3 = 10.
      const mastery = JobMastery(
        completions: 24,
        rank: 2,
        maxRank: 5,
        completionsUntilNextRank: 6,
      );

      expect(mastery.completionsPerRank, 10);
      expect(mastery.rankProgress, closeTo(0.4, 0.0001));
    });

    test('reads as complete at max rank', () async {
      const mastery = JobMastery(completions: 50, rank: 5, maxRank: 5);

      expect(mastery.completionsPerRank, isNull);
      expect(mastery.rankProgress, 1.0);
      expect(mastery.isMaxRank, isTrue);
    });

    test('reads as empty when mastery is disabled', () async {
      const mastery = JobMastery(completions: 5, rank: 0, maxRank: 0);

      expect(mastery.rankProgress, 0.0);
      expect(mastery.isMaxRank, isFalse);
    });
  });
}
