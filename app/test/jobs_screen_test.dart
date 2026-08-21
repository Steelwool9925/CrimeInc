import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps JobsScreen with a bootstrapped AppState.
Future<AppState> pumpJobs(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: JobsScreen(appState: state)));
  await tester.pumpAndSettle();

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

      expect(odds.data, contains('90%'));
    });

    testWidgets('a job shows the stats it requires', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      final odds = tester.widget<Text>(
          find.byKey(const Key('job-odds-shake-down-corner-store')));

      expect(odds.data, contains('3 attack'));
      expect(odds.data, contains('2 defense'));
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

    testWidgets('it says the job failed', (tester) async {
      await pumpJobs(tester, failing());

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();

      final title = tester.widget<Text>(find.byKey(const Key('job-result-title')));
      expect(title.data, isNot('Job complete'));
      expect(find.byKey(const Key('job-result-failed-note')), findsOneWidget);
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
}
