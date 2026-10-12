import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/theme/pack_theme.dart';
import 'package:underworld_empire/widgets/job_reveal.dart';

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
  Duration revealDelay = Duration.zero,
}) async {
  // Tall enough to build every card. The list is lazy and the result panel above it is always
  // present (a placeholder before the first attempt), so the default 800x600 surface no longer
  // reaches the last card.
  tester.view.physicalSize = const Size(800, 1400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final state = AppState(
    apiClient: api,
    tokenStore: InMemoryTokenStore(),
    revealDelay: revealDelay,
  );
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

      expect(lock.data, 'Needs 2 spare switchblades');
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
          matching: find.text('1 switchblade (1 spare left)'),
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

  group('JobsScreen — what an item-using job costs, before and after', () {
    FakeApiClient withJobs(List<Job> jobs, {DoJobResult? doJobResult}) => FakeApiClient(
          jobsResult: JobList(jobs: jobs, currentEnergy: 7, level: 3),
          doJobResult: doJobResult,
        );

    testWidgets('the card says what an attempt uses up, even while the job is locked', (tester) async {
      // The cost is known before the button is ever enabled. A locked job is exactly when the player
      // is deciding whether to go and get the item.
      await pumpJobs(tester, withJobs([kItemGatedJob]));

      final uses = tester.widget<Text>(find.byKey(const Key('job-uses-hijack-a-delivery-truck')));

      // The fixture's gate needs two, and the whole requirement is what an attempt uses up.
      expect(uses.data, contains('Uses up 2 switchblades each attempt'));
      expect(uses.data, contains('never the one you are wearing'));
    });

    testWidgets('the card says it when the job is available too', (tester) async {
      await pumpJobs(tester, withJobs([kItemReadyJob]));

      expect(find.byKey(const Key('job-uses-hijack-a-delivery-truck')), findsOneWidget);
    });

    testWidgets('a job that uses up nothing draws no such line', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('job-uses-shake-down-corner-store')), findsNothing);
    });

    testWidgets('the used-up tile names the loss in words, not a bare -1', (tester) async {
      await pumpJobs(
        tester,
        withJobs([kItemReadyJob],
            doJobResult: const DoJobResult(result: kItemConsumingJobResult, player: kFakeMe)),
      );

      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      final tile = find.byKey(const Key('job-result-item-switchblade'));
      expect(find.descendant(of: tile, matching: find.text('Used up')), findsOneWidget);
      expect(find.descendant(of: tile, matching: find.textContaining('switchblade')), findsOneWidget);
    });
  });

  group('JobsScreen — extra items for better odds', () {
    FakeApiClient withExtrasJob({DoJobResult? doJobResult}) => FakeApiClient(
          jobsResult: const JobList(jobs: [kExtrasJob], currentEnergy: 20, level: 3),
          doJobResult: doJobResult,
        );

    Finder more() => find.byKey(const Key('job-extras-more-hijack-a-delivery-truck-switchblade'));
    Finder less() => find.byKey(const Key('job-extras-less-hijack-a-delivery-truck-switchblade'));
    String count(WidgetTester tester) => tester
        .widget<Text>(find.byKey(const Key('job-extras-count-hijack-a-delivery-truck-switchblade')))
        .data!;

    testWidgets('an available item job offers a stepper that says what each extra is worth', (tester) async {
      await pumpJobs(tester, withExtrasJob());

      final label = tester.widget<Text>(
          find.byKey(const Key('job-extras-label-hijack-a-delivery-truck-switchblade')));
      expect(label.data, contains('Add extra switchblade'));
      expect(label.data, contains('+10% success each'));
      expect(label.data, contains('spent win or lose'));
      expect(count(tester), '0');
    });

    testWidgets('a locked job offers none', (tester) async {
      // A stepper on a card that cannot be attempted would invite choosing a cost for nothing.
      await pumpJobs(tester, FakeApiClient(
        jobsResult: const JobList(jobs: [kItemGatedJob], currentEnergy: 20, level: 3),
      ));

      expect(more(), findsNothing);
    });

    testWidgets('no spare beyond the requirement means nothing to add', (tester) async {
      await pumpJobs(tester, FakeApiClient(
        jobsResult: const JobList(jobs: [kItemReadyJob], currentEnergy: 20, level: 3),
      ));

      // kItemReadyJob holds exactly its requirement and allows no extras.
      expect(more(), findsNothing);
    });

    testWidgets('a job with no item gate has no stepper', (tester) async {
      await pumpJobs(tester, FakeApiClient());

      expect(
        find.byWidgetPredicate((w) =>
            w.key is ValueKey<String> && (w.key as ValueKey<String>).value.startsWith('job-extras-')),
        findsNothing,
      );
    });

    testWidgets('the stepper counts up to what is affordable and no further', (tester) async {
      await pumpJobs(tester, withExtrasJob());

      for (var i = 0; i < 5; i++) {
        await tester.tap(more(), warnIfMissed: false);
        await tester.pump();
      }

      expect(count(tester), '3', reason: 'held 4, needs 1: three spare, and the content allows three');
      expect(tester.widget<IconButton>(more()).onPressed, isNull);
    });

    testWidgets('the stepper cannot go below none', (tester) async {
      await pumpJobs(tester, withExtrasJob());

      expect(tester.widget<IconButton>(less()).onPressed, isNull);
      await tester.tap(more());
      await tester.pump();
      await tester.tap(less());
      await tester.pump();

      expect(count(tester), '0');
    });

    String oddsText(WidgetTester tester) =>
        tester.widget<Text>(find.byKey(const Key('job-odds-hijack-a-delivery-truck'))).data!;

    testWidgets('the odds on the card follow the extras dialled in', (tester) async {
      await pumpJobs(tester, withExtrasJob());
      expect(oddsText(tester), startsWith('65% success'));

      await tester.tap(more());
      await tester.pump();
      expect(oddsText(tester), startsWith('75% success'));

      await tester.tap(more());
      await tester.pump();
      await tester.tap(more());
      await tester.pump();
      expect(oddsText(tester), startsWith('95% success'));

      await tester.tap(less());
      await tester.pump();
      expect(oddsText(tester), startsWith('85% success'));
    });

    testWidgets('the previewed odds never pass the ceiling the server caps at', (tester) async {
      await pumpJobs(tester, FakeApiClient(
        jobsResult: const JobList(jobs: [kExtrasJobCapped], currentEnergy: 20, level: 3),
      ));

      for (var i = 0; i < 3; i++) {
        await tester.tap(more());
        await tester.pump();
      }

      expect(oddsText(tester), startsWith('80% success'));
    });

    testWidgets('the stepper row stays, inert, when the last spare is gone, so the card keeps its height',
        (tester) async {
      await pumpJobs(tester, withExtrasJob());
      final withSpares = tester.getSize(find.byKey(const Key('job-card-hijack-a-delivery-truck')));
      await tester.pumpWidget(const SizedBox());

      await pumpJobs(tester, FakeApiClient(
        jobsResult: const JobList(jobs: [kExtrasJobNoSpare], currentEnergy: 20, level: 3),
      ));

      expect(more(), findsOneWidget);
      expect(tester.widget<IconButton>(more()).onPressed, isNull);
      expect(tester.widget<IconButton>(less()).onPressed, isNull);
      expect(
        tester.widget<Text>(find.byKey(const Key('job-extras-label-hijack-a-delivery-truck-switchblade'))).data,
        contains('no spare copies'),
      );
      expect(tester.getSize(find.byKey(const Key('job-card-hijack-a-delivery-truck'))), withSpares);
    });

    testWidgets('a result that used several of an item says so in the plural', (tester) async {
      final api = FakeApiClient(
        jobsResult: const JobList(jobs: [kExtrasJob], currentEnergy: 20, level: 3),
        doJobResult: const DoJobResult(result: kExtrasConsumingJobResult, player: kFakeMe),
      );
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(
        find.descendant(
          of: find.byKey(const Key('job-result-item-switchblade')),
          matching: find.text('4 switchblades (0 spare left)'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('leaving Jobs forgets the result, so coming back shows the placeholder', (tester) async {
      final state = await pumpJobs(tester, FakeApiClient(
        jobsResult: const JobList(jobs: [kExtrasJob], currentEnergy: 20, level: 3),
        doJobResult: const DoJobResult(result: kExtrasConsumingJobResult, player: kFakeMe),
      ));
      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();
      expect(state.lastJobResult, isNotNull);

      await tester.pumpWidget(const SizedBox());
      expect(state.lastJobResult, isNull);

      await tester.pumpWidget(MaterialApp(
        home: JobsScreen(appState: state, refreshInterval: const Duration(hours: 1)),
      ));
      await tester.pumpAndSettle();
      expect(find.text('Job complete'), findsNothing);
      expect(find.byKey(const Key('job-result-item-switchblade')), findsNothing);
    });

    testWidgets('the attempt is made with the extras that were dialled in', (tester) async {
      final api = withExtrasJob();
      await pumpJobs(tester, api);

      await tester.tap(more());
      await tester.pump();
      await tester.tap(more());
      await tester.pump();
      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(api.doJobExtras, [
        {'switchblade': 2},
      ]);
    });

    testWidgets('an attempt with nothing dialled in sends no extras', (tester) async {
      final api = withExtrasJob();
      await pumpJobs(tester, api);

      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(api.doJobExtras, [<String, int>{}]);
    });

    testWidgets('the selection does not carry over to the next attempt', (tester) async {
      // Extras are spent, so a leftover selection would quietly pad an attempt the player did not mean to.
      final api = withExtrasJob();
      await pumpJobs(tester, api);

      await tester.tap(more());
      await tester.pump();
      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(api.doJobExtras, [
        {'switchblade': 1},
        <String, int>{},
      ]);
      expect(count(tester), '0');
    });

    testWidgets('the stepper buttons carry names a screen reader can use', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, withExtrasJob());

      expect(find.byTooltip('One more extra switchblade'), findsOneWidget);
      expect(find.byTooltip('One fewer extra switchblade'), findsOneWidget);
      // Unanchored: the widget-test tree folds the whole card into one node.
      expect(find.bySemanticsLabel(RegExp(r'0 extra switchblade, up to 3')), findsWidgets);
      semantics.dispose();
    });
  });

  group('JobsScreen — the list does not move when a result appears', () {
    Future<Offset> doJobButton(WidgetTester tester) async =>
        tester.getTopLeft(find.byKey(const Key('do-job-shake-down-corner-store')));

    testWidgets('the Do job button stays put before the first attempt, after it, and after a dismissal',
        (tester) async {
      // The result panel sits above the list, so a panel that appeared from nothing pushed every
      // button down by its own height on a session's first attempt — two of three quick taps at the
      // same spot then landed on the banner. The invariant that matters is the button's position, not
      // the panel's size.
      await pumpJobs(tester, FakeApiClient());
      final before = await doJobButton(tester);
      expect(find.byKey(const Key('job-result-placeholder')), findsOneWidget);
      expect(find.byKey(const Key('job-result-panel')), findsNothing);

      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pumpAndSettle();
      final after = await doJobButton(tester);
      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);

      await tester.tap(find.byKey(const Key('dismiss-job-result')));
      await tester.pumpAndSettle();
      final dismissed = await doJobButton(tester);

      expect(after, before, reason: 'the first result moved the list');
      expect(dismissed, before, reason: 'dismissing the result moved the list');
    });

    testWidgets('the placeholder offers nothing to press and says nothing misleading', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, FakeApiClient());

      // Its dismiss button is laid out (to keep the row's height) but hidden and inert.
      expect(find.bySemanticsLabel('Dismiss'), findsNothing);
      expect(find.text('Your latest job shows up here.'), findsOneWidget);
      semantics.dispose();
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

  group('JobsScreen — the reveal', () {
    const skipKey = Key('job-result-skip');
    const levelUp = LevelUp(
      levelsGained: 1,
      newLevel: 2,
      skillPointsGranted: 1,
      metersRefilled: true,
    );
    const won = DoJobResult(result: kSucceededJobResult, player: kFakeMe);
    const lost = DoJobResult(result: kFailedJobResult, player: kFakeMe);
    const wonAndLevelled = DoJobResult(
      result: kSucceededJobResult,
      player: kFakeMe,
      levelUp: levelUp,
    );

    /// Taps the job, lets the request resolve, and builds the first frame after it.
    Future<void> tapJob(WidgetTester tester) async {
      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pump();
      await tester.pump();
    }

    testWidgets('holds the outcome back, showing suspense instead of the result', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: won),
          revealDelay: const Duration(milliseconds: 300));

      await tapJob(tester);

      expect(find.byKey(const Key('job-result-anticipating')), findsOneWidget);
      expect(find.text('Moment of truth'), findsOneWidget);
      expect(find.byKey(const Key('job-result-panel')), findsNothing);

      await tester.pump(const Duration(milliseconds: 300));
      await tester.pump();
      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);
      expect(find.byKey(const Key('job-result-anticipating')), findsNothing);
      await tester.pumpAndSettle();
    });

    testWidgets('a tap during the suspense skips straight to the result', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: won),
          revealDelay: const Duration(hours: 1));
      await tapJob(tester);
      expect(find.byKey(const Key('job-result-panel')), findsNothing);

      await tester.tap(find.byKey(skipKey));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);
      await tester.pumpAndSettle();
    });

    testWidgets('the panel keeps its height through suspense and reveal', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: won),
          revealDelay: const Duration(hours: 1));
      final resting = tester.getSize(find.byKey(const Key('job-result-placeholder')));

      await tapJob(tester);
      expect(tester.getSize(find.byKey(const Key('job-result-anticipating'))), resting);

      await tester.tap(find.byKey(skipKey));
      await tester.pump();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));
      expect(tester.getSize(find.byKey(const Key('job-result-panel'))), resting);
      await tester.pumpAndSettle();
      expect(tester.getSize(find.byKey(const Key('job-result-panel'))), resting);
    });

    testWidgets('the numbers count up from zero rather than appearing at once', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: won));

      await tapJob(tester);
      expect(find.text('+25'), findsNothing);

      await tester.pumpAndSettle();
      expect(find.text('+25'), findsOneWidget);
    });

    testWidgets('a tile does not change width while its number counts up', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: lost));
      const health = Key('job-result-health');

      await tapJob(tester);
      await tester.pump(const Duration(milliseconds: 300));
      final midReveal = tester.getSize(find.byKey(health)).width;

      await tester.pumpAndSettle();
      expect(tester.getSize(find.byKey(health)).width, midReveal);
    });

    testWidgets('a level-up waits for the reveal to finish, then crowns it', (tester) async {
      final state = await pumpJobs(tester, FakeApiClient(doJobResult: wonAndLevelled));

      await tapJob(tester);
      expect(find.byKey(const Key('job-result-panel')), findsOneWidget);
      expect(find.byKey(const Key('level-up-dialog')), findsNothing);

      await tester.pump(const Duration(milliseconds: 300));
      expect(find.byKey(const Key('level-up-dialog')), findsNothing);

      await tester.pumpAndSettle();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
      expect(state.lastJobResult, isNotNull);
    });

    testWidgets('a level-up is not held back when the result was dismissed first', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: wonAndLevelled));

      await tapJob(tester);
      await tester.tap(find.byKey(const Key('dismiss-job-result')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
    });

    testWidgets('with animations disabled the result and level-up land without animation time',
        (tester) async {
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
      await pumpJobs(tester, FakeApiClient(doJobResult: wonAndLevelled));

      await tapJob(tester);
      expect(find.text('+25'), findsOneWidget);

      await tester.pump();
      await tester.pump();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
    });

    group('success and failure differ without colour', () {
      /// How far the verdict sits from its resting place a moment into the reveal: positive is
      /// below it, negative is above.
      Future<double> verdictOffsetEarly(WidgetTester tester, DoJobResult outcome) async {
        await pumpJobs(tester, FakeApiClient(doJobResult: outcome));
        await tapJob(tester);
        await tester.pump(const Duration(milliseconds: 40));
        final early = tester.getTopLeft(find.byKey(const Key('job-result-title'))).dy;
        await tester.pumpAndSettle();
        return early - tester.getTopLeft(find.byKey(const Key('job-result-title'))).dy;
      }

      testWidgets('a success rises into place', (tester) async {
        expect(await verdictOffsetEarly(tester, won), greaterThan(0));
      });

      testWidgets('a failure drops into place', (tester) async {
        expect(await verdictOffsetEarly(tester, lost), lessThan(0));
      });

      testWidgets('speedlines fire on a success only', (tester) async {
        await pumpJobs(tester, FakeApiClient(doJobResult: won));
        await tapJob(tester);
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('job-result-speedlines')), findsOneWidget);
        expect(find.byKey(const Key('job-result-halftone')), findsNothing);
      });

      testWidgets('a failure darkens the halftone and fires no speedlines', (tester) async {
        await pumpJobs(tester, FakeApiClient(doJobResult: lost));
        await tapJob(tester);
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('job-result-halftone')), findsOneWidget);
        expect(find.byKey(const Key('job-result-speedlines')), findsNothing);
      });

      testWidgets('both keep their written verdict', (tester) async {
        await pumpJobs(tester, FakeApiClient(doJobResult: won));
        await tapJob(tester);
        await tester.pumpAndSettle();
        expect(find.text('Job complete'), findsOneWidget);
      });
    });
  });

  group('JobsScreen — a bigger win is staged bigger', () {
    const lost = DoJobResult(result: kFailedJobResult, player: kFakeMe);

    // The fake job pays 15-30: 15 is a scrape, 25 a good roll, 30 the top of the range.
    DoJobResult winPaying(int cash, {LevelUp? levelUp}) => DoJobResult(
          result: JobResult(
            jobId: 'shake-down-corner-store',
            succeeded: true,
            energySpent: 1,
            cashEarned: cash,
            xpEarned: 10,
            healthLost: 0,
            mastery: kFakeMastery,
            masteryRankedUp: false,
          ),
          player: kFakeMe,
          levelUp: levelUp,
        );

    Future<void> tapJob(WidgetTester tester) async {
      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pump();
      await tester.pump();
    }

    const speedlines = Key('job-result-speedlines');
    const flash = Key('job-result-flash');

    testWidgets('a scrape is the verdict alone: numbers already there, no speedlines, no flash',
        (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: winPaying(15)));

      await tapJob(tester);

      expect(find.text('Job complete'), findsOneWidget);
      expect(find.text('+15'), findsOneWidget);
      expect(find.byKey(speedlines), findsNothing);
      expect(find.byKey(flash), findsNothing);

      // The whole reveal is the stamp, so it is over well before a staged one would be.
      final reveal = tester.widget<JobReveal<JobResult>>(find.byType(JobReveal<JobResult>));
      expect(reveal.duration, PackMotion.standard.successDuration);
    });

    testWidgets('a good roll adds speedlines and counts its numbers up, with no flash',
        (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: winPaying(25)));

      await tapJob(tester);
      expect(find.text('+25'), findsNothing);

      await tester.pumpAndSettle();
      expect(find.text('+25'), findsOneWidget);
      expect(find.byKey(speedlines), findsOneWidget);
      expect(find.byKey(flash), findsNothing);
    });

    testWidgets('a top-of-the-range roll adds the halftone flash to everything a good one has',
        (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: winPaying(30)));

      await tapJob(tester);
      await tester.pump(const Duration(milliseconds: 300));
      expect(find.byKey(flash), findsOneWidget);

      await tester.pumpAndSettle();
      expect(find.byKey(speedlines), findsOneWidget);
      expect(find.byKey(flash), findsOneWidget);
    });

    testWidgets('a failure gets no win staging, however much it paid', (tester) async {
      await pumpJobs(tester, FakeApiClient(doJobResult: lost));

      await tapJob(tester);
      await tester.pumpAndSettle();

      expect(find.byKey(flash), findsNothing);
      expect(find.byKey(speedlines), findsNothing);
    });

    testWidgets('acknowledging the level-up does not shrink the reveal already on screen',
        (tester) async {
      await pumpJobs(
        tester,
        FakeApiClient(
          doJobResult: winPaying(30,
              levelUp: const LevelUp(
                levelsGained: 1,
                newLevel: 2,
                skillPointsGranted: 1,
                metersRefilled: true,
              )),
        ),
      );

      await tapJob(tester);
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);

      await tester.tap(find.byKey(const Key('level-up-ok')));
      await tester.pumpAndSettle();

      expect(find.byKey(flash), findsOneWidget);
    });

    testWidgets('a scrape that levels the player up is lifted, so the level-up is not undersold',
        (tester) async {
      await pumpJobs(
        tester,
        FakeApiClient(
          doJobResult: winPaying(15,
              levelUp: const LevelUp(
                levelsGained: 1,
                newLevel: 2,
                skillPointsGranted: 1,
                metersRefilled: true,
              )),
        ),
      );

      await tapJob(tester);
      await tester.pumpAndSettle();

      expect(find.byKey(speedlines), findsOneWidget);
    });
  });

  group('JobsScreen — the reveal is contained', () {
    testWidgets('a full reveal rebuilds the panel but never a job card', (tester) async {
      await pumpJobs(
        tester,
        FakeApiClient(doJobResult: const DoJobResult(result: kSucceededJobResult, player: kFakeMe)),
      );
      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pump();
      await tester.pump();

      // The result has just landed, so the cards have rebuilt for it. From here only the animation
      // is ticking; counting rebuilds across the whole reveal shows who it disturbs.
      var cards = 0;
      var ticks = 0;
      final previous = debugOnRebuildDirtyWidget;
      debugOnRebuildDirtyWidget = (element, builtOnce) {
        final type = element.widget.runtimeType.toString();
        if (type == '_JobCard') cards++;
        if (type == 'AnimatedBuilder') ticks++;
      };
      addTearDown(() => debugOnRebuildDirtyWidget = previous);

      for (var i = 0; i < 60; i++) {
        await tester.pump(const Duration(milliseconds: 16));
      }

      expect(ticks, greaterThan(20), reason: 'the reveal should really have been animating');
      expect(cards, 0);
    });
  });
}
