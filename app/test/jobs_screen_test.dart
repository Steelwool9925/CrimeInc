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
            energySpent: 1,
            cashEarned: 25,
            xpEarned: 10,
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
            energySpent: 1,
            cashEarned: 25,
            xpEarned: 10,
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
            energySpent: 1,
            cashEarned: 25,
            xpEarned: 500,
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
}
