import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps JobsScreen with a bootstrapped AppState.
///
/// [refreshInterval] is pushed far out by default so a test that isn't about the periodic re-read
/// is never disturbed by an extra `/jobs` call mid-test — mirrors `pumpProperties`'s identical
/// reasoning for `PropertiesScreen`. The tests that are about the re-read set it themselves.
Future<AppState> pumpJobs(
  WidgetTester tester,
  FakeApiClient api, {
  Duration refreshInterval = const Duration(hours: 1),
}) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(
    home: JobsScreen(appState: state, refreshInterval: refreshInterval),
  ));
  await tester.pumpAndSettle();

  // Tear the tree down inside the test's own zone, so the screen's periodic timer is cancelled
  // before the binding checks for timers still pending — same reasoning as `pumpProperties`.
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));

  return state;
}

void main() {
  group('JobsScreen — catalog', () {
    testWidgets('renders a card per job', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('job-card-shake-down-corner-store')), findsOneWidget);
      expect(find.byKey(const Key('job-card-hijack-a-delivery-truck')), findsOneWidget);
      expect(find.byKey(const Key('job-card-rob-an-armored-transport')), findsOneWidget);
    });

    testWidgets('shows the cash range and xp from content', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.textContaining('15–30'), findsOneWidget);
      expect(find.textContaining('10 XP'), findsOneWidget);
    });

    testWidgets('loads the catalog on first frame', (tester) async {
      final api = FakeApiClient();
      await pumpJobs(tester, api);

      expect(api.jobsCalls, 1);
    });

    testWidgets('shows a mastery bar per job', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('mastery-bar-shake-down-corner-store')), findsOneWidget);
    });

    testWidgets('shows the current energy in the app bar', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('jobs-energy')), findsOneWidget);
      expect(find.text('Energy 7/10'), findsOneWidget);
    });
  });

  group('JobsScreen — gating', () {
    testWidgets('an attemptable job has an enabled button', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      final button = tester.widget<ElevatedButton>(
        find.byKey(const Key('do-job-shake-down-corner-store')),
      );
      expect(button.onPressed, isNotNull);
    });

    testWidgets('a level-locked job has a disabled button', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      final button = tester.widget<ElevatedButton>(
        find.byKey(const Key('do-job-rob-an-armored-transport')),
      );
      expect(button.onPressed, isNull);
    });

    testWidgets('a level-locked job explains the level requirement', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(
        find.byKey(const Key('job-lock-rob-an-armored-transport')),
        findsOneWidget,
      );
      expect(find.text('Requires level 8'), findsOneWidget);
    });

    testWidgets('an unaffordable job explains the energy cost', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.text('Needs 4 energy'), findsOneWidget);
    });

    testWidgets('an attemptable job shows no lock label', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(
        find.byKey(const Key('job-lock-shake-down-corner-store')),
        findsNothing,
      );
    });
  });

  group('JobsScreen — doing a job', () {
    testWidgets('tapping Do job calls the api with that job id', (tester) async {
      final api = FakeApiClient();
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(api.doJobCalls, ['shake-down-corner-store']);
    });

    testWidgets('the result panel appears with the realised payout', (tester) async {
      final api = FakeApiClient();
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);
      expect(find.text('+22'), findsOneWidget); // the rolled cash
      expect(find.text('+10'), findsOneWidget); // xp
      expect(find.text('-1'), findsOneWidget);  // energy
    });

    testWidgets('the result panel can be dismissed', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('dismiss-job-result')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-panel')), findsNothing);
    });

    testWidgets('doing a job does not trigger a follow-up /me call', (tester) async {
      // The do-job response carries the post-action projection; a follow-up would defeat the point.
      final api = FakeApiClient();
      await pumpJobs(tester, api);
      final meCallsBefore = api.meCalls;

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(api.meCalls, meCallsBefore);
    });
  });

  group('JobsScreen — refusals', () {
    testWidgets('an energy refusal shows a readable message', (tester) async {
      final api = FakeApiClient(refuseDoJobWith: 'energy');
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.text('Not enough energy.'), findsOneWidget);
    });

    testWidgets('a level refusal shows a readable message', (tester) async {
      final api = FakeApiClient(refuseDoJobWith: 'level');
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.text('Your level is too low for that job.'), findsOneWidget);
    });

    testWidgets('a refusal does not show the result panel', (tester) async {
      final api = FakeApiClient(refuseDoJobWith: 'energy');
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-panel')), findsNothing);
    });
  });

  group('JobsScreen — level up', () {
    testWidgets('a level-up shows the dialog with the granted point', (tester) async {
      final api = FakeApiClient(
        doJobResult: const DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: 25,
            xpEarned: 10,
            healthLost: 0,
            mastery: JobMastery(
              completions: 10,
              rank: 1,
              maxRank: 5,
              completionsUntilNextRank: 10,
            ),
            masteryRankedUp: true,
          ),
          player: kFakeMe,
          levelUp: LevelUp(
            levelsGained: 1,
            newLevel: 2,
            skillPointsGranted: 1,
            metersRefilled: true,
          ),
        ),
      );
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
      expect(find.text('Level 2!'), findsOneWidget);
      expect(find.text('You earned 1 skill point.'), findsOneWidget);
      expect(find.text('Energy, stamina and health refilled.'), findsOneWidget);
    });

    testWidgets('acknowledging the dialog closes it', (tester) async {
      final api = FakeApiClient(
        doJobResult: const DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: 25,
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
      );
      final state = await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('level-up-ok')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
      expect(state.pendingLevelUp, isNull);
    });

    testWidgets('a refresh tick while the dialog is open does not stack a second copy',
        (tester) async {
      // Regression. `_maybeShowLevelUp` is called from `build`, and `build` re-runs on every
      // `notifyListeners` — including the periodic re-read. It used to push a *fresh* dialog on
      // each of those rebuilds while `pendingLevelUp` was still set, so a player who took longer
      // than one tick to dismiss it ended up behind a stack of identical copies: tapping "Nice"
      // popped the top one and revealed the next, which reads as a button that does nothing.
      //
      // This went unseen because every other test in this file pushes `refreshInterval` out to an
      // hour, so no rebuild ever landed while the dialog was open. This one deliberately does not.
      final api = FakeApiClient(
        doJobResult: const DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: 25,
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
      );
      final state = await pumpJobs(tester, api, refreshInterval: const Duration(seconds: 10));

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);

      // Three re-reads land while the dialog is still up — roughly the half-minute a real player
      // spends reading it. Still exactly one dialog.
      for (var i = 0; i < 3; i++) {
        await tester.pump(const Duration(seconds: 10));
        await tester.pumpAndSettle();
      }
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);

      // And a single tap is enough to be rid of it.
      await tester.tap(find.byKey(const Key('level-up-ok')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
      expect(state.pendingLevelUp, isNull);
    });

    testWidgets('a multi-level gain says so', (tester) async {
      final api = FakeApiClient(
        doJobResult: const DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: 25,
            xpEarned: 500,
            healthLost: 0,
            mastery: kFakeMastery,
            masteryRankedUp: false,
          ),
          player: kFakeMe,
          levelUp: LevelUp(
            levelsGained: 3,
            newLevel: 4,
            skillPointsGranted: 3,
            metersRefilled: true,
          ),
        ),
      );
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.text('Level 4! (+3 levels)'), findsOneWidget);
      expect(find.text('You earned 3 skill points.'), findsOneWidget);
    });

    testWidgets('no level-up means no dialog', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
    });
  });

  group('JobsScreen — errors', () {
    testWidgets('a catalog failure shows an error and a retry', (tester) async {
      await pumpJobs(tester, FakeApiClient(throwOnJobs: true));

      expect(find.byKey(const Key('jobs-error')), findsOneWidget);
      expect(find.byKey(const Key('jobs-retry')), findsOneWidget);
    });

    testWidgets('retry re-requests the catalog', (tester) async {
      final api = FakeApiClient(throwOnJobs: true);
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('jobs-retry')));
      await tester.pumpAndSettle();

      expect(api.jobsCalls, 2);
    });
  });

  group('JobsScreen — risk', () {
    /// A catalog whose second row is stat-locked rather than level- or energy-locked.
    FakeApiClient withStatLockedJob() => FakeApiClient(
          jobsResult: const JobList(
            jobs: [kCheapJob, kStatLockedJob],
            currentEnergy: 7,
            level: 2,
          ),
        );

    testWidgets('a job shows its success chance as a percentage', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      final odds = tester.widget<Text>(
          find.byKey(const Key('job-odds-shake-down-corner-store')));

      expect(odds.data, contains('65%'));
    });

    testWidgets('a job shows the stats it requires', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      final odds = tester.widget<Text>(
          find.byKey(const Key('job-odds-shake-down-corner-store')));

      expect(odds.data, contains('1 attack'));
      expect(odds.data, contains('1 defense'));
    });

    testWidgets('a stat-locked job is disabled', (tester) async {
      await pumpJobs(tester, withStatLockedJob());

      final button = tester.widget<ElevatedButton>(
          find.byKey(const Key('do-job-run-numbers-for-the-bookie')));

      expect(button.onPressed, isNull);
    });

    testWidgets('a stat-locked job names the stats it needs, not the level', (tester) async {
      // The player is level 2 here, so "requires level 2" would be both wrong and useless — the
      // thing standing in the way is a point of attack, and equipping something fixes it.
      await pumpJobs(tester, withStatLockedJob());

      final lock = tester.widget<Text>(
          find.byKey(const Key('job-lock-run-numbers-for-the-bookie')));

      expect(lock.data, contains('6 attack'));
      expect(lock.data, contains('5 defense'));
    });
  });

  group('JobsScreen — item gate', () {
    /// A catalog whose second row is item-locked rather than level-, stats- or energy-locked.
    FakeApiClient withItemGatedJob() => FakeApiClient(
          jobsResult: const JobList(
            jobs: [kCheapJob, kItemGatedJob],
            currentEnergy: 7,
            level: 3,
          ),
        );

    testWidgets('an item-locked job is disabled', (tester) async {
      await pumpJobs(tester, withItemGatedJob());

      final button = tester.widget<ElevatedButton>(
          find.byKey(const Key('do-job-hijack-a-delivery-truck')));

      expect(button.onPressed, isNull);
    });

    testWidgets('an item-locked job names the item it needs', (tester) async {
      await pumpJobs(tester, withItemGatedJob());

      final lock = tester.widget<Text>(
          find.byKey(const Key('job-lock-hijack-a-delivery-truck')));

      expect(lock.data, 'Needs 2 switchblade');
    });

    testWidgets('a job with no item requirements shows no item wording', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      // kUnaffordableJob shares the same id but is locked on energy, with no item requirements —
      // this must never read as an item lock.
      final lock = tester.widget<Text>(
          find.byKey(const Key('job-lock-hijack-a-delivery-truck')));

      expect(lock.data, isNot(contains('switchblade')));
    });

    testWidgets('a successful item-gated attempt shows the consumption in the completion card',
        (tester) async {
      final api = FakeApiClient(
        jobsResult: const JobList(
          jobs: [kItemReadyJob],
          currentEnergy: 7,
          level: 3,
        ),
        doJobResult: const DoJobResult(
          result: kItemConsumingJobResult,
          player: kFakeMe,
        ),
      );
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-item-switchblade')), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(const Key('job-result-item-switchblade')),
          matching: find.text('-1 (1 left)'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('a job with no item requirements draws no item tile', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-item-switchblade')), findsNothing);
    });
  });

  group('JobsScreen — a failed attempt', () {
    FakeApiClient failing() => FakeApiClient(
          doJobResult: const DoJobResult(
            result: kFailedJobResult,
            player: kFakeMe,
          ),
        );

    testWidgets('the result panel opens rather than an error', (tester) async {
      await pumpJobs(tester, failing());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);
      expect(find.byKey(const Key('action-message')), findsNothing);
    });

    testWidgets('the panel is the same height whichever way the job goes', (tester) async {
      // Regression, and the reason the trailing lines in JobResultPanel are always laid out.
      // The panel sits above the job list, so a change in its height moves every "Do job" button
      // under it. A failure used to add an outcome note that a success did not have, and a
      // rank-up added another line again, so the primary button jumped by about a line between
      // consecutive taps — on the control a new player presses roughly thirty times in their
      // first session. These three outcomes must occupy exactly the same box.
      Future<double> panelHeight(FakeApiClient api) async {
        // Tear the previous tree down first. Pumping a second JobsScreen into the same slot lets
        // Flutter reuse `_JobsScreenState`, so `initState` — and the `loadJobs` it schedules —
        // never runs again, and the screen renders against an AppState that has no jobs yet.
        await tester.pumpWidget(const SizedBox.shrink());
        await pumpJobs(tester, api);
        await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
        await tester.pumpAndSettle();
        return tester.getSize(find.byKey(const Key('job-result-panel'))).height;
      }

      final failed = await panelHeight(failing());
      final succeeded = await panelHeight(FakeApiClient(
        doJobResult: const DoJobResult(result: kSucceededJobResult, player: kFakeMe),
      ));
      final rankedUp = await panelHeight(FakeApiClient(
        doJobResult: const DoJobResult(
          result: kSucceededJobResultWithRankUp,
          player: kFakeMe,
        ),
      ));

      expect(succeeded, failed,
          reason: 'a success and a failure must not move the job list relative to each other');
      expect(rankedUp, succeeded,
          reason: 'a mastery rank-up must not move the job list either');
    });

    testWidgets('it says the job failed', (tester) async {
      await pumpJobs(tester, failing());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      final title = tester.widget<Text>(find.byKey(const Key('job-result-title')));
      expect(title.data, isNot('Job complete'));
      // The note line is always laid out now (it keeps the panel one height — see
      // JobResultPanel), so assert on what it *says* rather than merely that it exists.
      final note = tester.widget<Text>(find.byKey(const Key('job-result-note')));
      expect(note.data, contains('The job failed'));
    });

    testWidgets('it shows the consolation payout and the health lost', (tester) async {
      await pumpJobs(tester, failing());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('job-result-health')), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(const Key('job-result-health')),
          matching: find.text('-3'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('a successful attempt draws no health tile', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      // Otherwise every successful job would read "Health -0".
      expect(find.byKey(const Key('job-result-health')), findsNothing);
    });
  });

  group('JobsScreen — refreshing', () {
    testWidgets('re-reads the catalog on its refresh interval', (tester) async {
      final api = FakeApiClient();
      await pumpJobs(tester, api, refreshInterval: const Duration(seconds: 10));

      await tester.pump(const Duration(seconds: 10));
      await tester.pump();
      await tester.pump();

      // Energy regenerates and gear-driven stats change with nothing that visits this screen to
      // say so, so it keeps asking — the same reasoning PropertiesScreen already re-reads for.
      expect(api.jobsCalls, 2);
    });

    testWidgets('does not poll again while an action is in flight', (tester) async {
      final api = FakeApiClient()..doJobGate = Completer<void>();
      await pumpJobs(tester, api, refreshInterval: const Duration(seconds: 10));

      // Dispatches the tap and lets doJob's onPressed closure run; the request itself stays open
      // on doJobGate.
      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pump();

      // The tick lands while that request is still open.
      await tester.pump(const Duration(seconds: 10));
      expect(api.jobsCalls, 1);

      api.doJobGate!.complete();
      await tester.pumpAndSettle();
    });
  });
}
