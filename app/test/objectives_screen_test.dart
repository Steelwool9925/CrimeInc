import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/screens/objectives_screen.dart';
import 'package:underworld_empire/screens/properties_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// The instant the countdown is measured from: five hours and twelve minutes before the fake reset.
final _now = DateTime.utc(2026, 10, 13, 18, 48);

Future<AppState> pumpObjectives(
  WidgetTester tester,
  FakeApiClient api, {
  Duration refreshInterval = const Duration(hours: 1),
}) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(
    home: ObjectivesScreen(
      appState: state,
      refreshInterval: refreshInterval,
      clock: () => _now,
    ),
  ));
  await tester.pump();
  await tester.pump();

  // Cancel the screen's refresh timer inside the test's own zone.
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  return state;
}

void main() {
  group('ObjectivesScreen — list', () {
    testWidgets('draws a card per objective, with its name and reward', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      expect(find.text('Daily objectives'), findsOneWidget);
      expect(find.byKey(const Key('objective-card-corner-store-regular')), findsOneWidget);
      expect(find.byKey(const Key('objective-card-two-different-jobs')), findsOneWidget);
      expect(find.byKey(const Key('objective-card-busy-day')), findsOneWidget);
      expect(find.text('Corner Store Regular'), findsOneWidget);
      expect(find.byKey(const Key('objective-reward-corner-store-regular')), findsOneWidget);
    });

    testWidgets('states the reward in the pack currency and XP', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      final reward = tester.widget<Text>(find.byKey(const Key('objective-reward-corner-store-regular')));
      expect(reward.data, contains('110'));
      expect(reward.data, contains('25 XP'));
    });

    testWidgets('shows progress as a bar and a count', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      final bar = tester.widget<LinearProgressIndicator>(
          find.byKey(const Key('objective-bar-corner-store-regular')));
      expect(bar.value, closeTo(0.4, 1e-9));
      expect(find.text('2/5'), findsOneWidget);
    });

    testWidgets('keeps the server order rather than moving finished ones around', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      final ys = [
        for (final id in ['corner-store-regular', 'two-different-jobs', 'busy-day'])
          tester.getTopLeft(find.byKey(Key('objective-card-$id'))).dy,
      ];
      expect(ys, orderedEquals([...ys]..sort()));
    });

    testWidgets('says when the next set arrives', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      expect(find.byKey(const Key('objectives-reset')), findsOneWidget);
      expect(find.text('Resets in 5h 12m'), findsOneWidget);
    });
  });

  group('ObjectivesScreen — what each card offers', () {
    testWidgets('an unfinished objective offers a way to work on it, not a claim', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      expect(find.byKey(const Key('go-objective-corner-store-regular')), findsOneWidget);
      expect(find.byKey(const Key('claim-objective-corner-store-regular')), findsNothing);
    });

    testWidgets('a finished objective offers Claim', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      expect(find.byKey(const Key('claim-objective-two-different-jobs')), findsOneWidget);
      expect(find.byKey(const Key('go-objective-two-different-jobs')), findsNothing);
    });

    testWidgets('a claimed objective says so in words and offers nothing', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      final claimed = find.byKey(const Key('objective-claimed-busy-day'));
      expect(claimed, findsOneWidget);
      expect(find.descendant(of: claimed, matching: find.text('Claimed')), findsOneWidget);
      expect(find.byKey(const Key('claim-objective-busy-day')), findsNothing);
      expect(find.byKey(const Key('go-objective-busy-day')), findsNothing);
    });
  });

  group('ObjectivesScreen — claiming', () {
    testWidgets('tapping Claim pays out and the card becomes Claimed', (tester) async {
      final api = FakeApiClient();
      final state = await pumpObjectives(tester, api);
      final before = state.me!.cash;

      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();

      expect(api.claimObjectiveCalls, ['two-different-jobs']);
      expect(state.me!.cash, before + 60);
      expect(find.byKey(const Key('objective-claimed-two-different-jobs')), findsOneWidget);
      expect(find.byKey(const Key('claim-objective-two-different-jobs')), findsNothing);
    });

    testWidgets('a refusal shows a banner that can be dismissed', (tester) async {
      await pumpObjectives(tester, FakeApiClient(refuseClaimWith: 'notComplete'));

      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.text('That objective is not finished yet.'), findsOneWidget);

      await tester.tap(find.text('Dismiss'));
      await tester.pump();
      expect(find.byKey(const Key('action-message')), findsNothing);
    });

    testWidgets('a reward that levels the player up shows the level-up dialog', (tester) async {
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
      await pumpObjectives(tester, api);

      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();
      await tester.pump();

      expect(find.byType(AlertDialog), findsOneWidget);
    });

    testWidgets('while a claim is in flight every Claim button is disabled, and one goes out', (tester) async {
      final api = FakeApiClient()..claimGate = Completer<void>();
      await pumpObjectives(tester, api);
      final claim = find.byKey(const Key('claim-objective-two-different-jobs'));

      await tester.tap(claim);
      await tester.pump();

      expect(tester.widget<FilledButton>(claim).onPressed, isNull);
      expect(api.claimObjectiveCalls, hasLength(1));

      api.claimGate!.complete();
      await tester.pump();
      await tester.pump();
      expect(find.byKey(const Key('objective-claimed-two-different-jobs')), findsOneWidget);
    });
  });

  group('ObjectivesScreen — level-up dialog', () {
    ClaimObjectiveResult levelUpClaim() => ClaimObjectiveResult(
          claimed: kObjectiveClaimed,
          player: kObjectiveClaimedMe,
          levelUp: const LevelUp(
            levelsGained: 1,
            newLevel: 3,
            skillPointsGranted: 1,
            metersRefilled: true,
          ),
          objectives: const [kObjectiveClaimed],
        );

    testWidgets('tapping Nice closes it and clears the pending level-up', (tester) async {
      final state = await pumpObjectives(tester, FakeApiClient(claimObjectiveResult: levelUpClaim()));

      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();
      await tester.pump();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
      expect(state.pendingLevelUp, isNotNull);

      await tester.tap(find.byKey(const Key('level-up-ok')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
      expect(state.pendingLevelUp, isNull);
    });

    testWidgets('dismissing it by tapping outside also clears it, so it does not reopen', (tester) async {
      final state = await pumpObjectives(tester, FakeApiClient(claimObjectiveResult: levelUpClaim()));

      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();
      await tester.pump();
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);

      await tester.tapAt(const Offset(2, 2));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
      expect(state.pendingLevelUp, isNull);
      // A later rebuild must not bring it back.
      await tester.pump(const Duration(seconds: 1));
      expect(find.byKey(const Key('level-up-dialog')), findsNothing);
    });

    testWidgets(
        'a level-up from a job done on Jobs, reached through Go to Jobs, waits for the reveal '
        'instead of covering it', (tester) async {
      final api = FakeApiClient(
        doJobResult: const DoJobResult(
          result: kSucceededJobResult,
          player: kFakeMe,
          levelUp: LevelUp(levelsGained: 1, newLevel: 2, skillPointsGranted: 1, metersRefilled: true),
        ),
      );
      final state = AppState(
        apiClient: api,
        tokenStore: InMemoryTokenStore(),
        revealDelay: const Duration(milliseconds: 400),
      );
      await state.bootstrap();
      await tester.pumpWidget(MaterialApp(
        home: ObjectivesScreen(appState: state, refreshInterval: const Duration(hours: 1), clock: () => _now),
      ));
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
      await tester.pump();
      await tester.pump();

      await tester.tap(find.byKey(const Key('go-objective-corner-store-regular')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));
      await tester.tap(find.byKey(const Key('do-job-shake-down-corner-store')));
      await tester.pump();

      // Through the hold, and the instant the result is published: no dialog yet.
      await tester.pump(const Duration(milliseconds: 100));
      await tester.pump(const Duration(milliseconds: 350));
      await tester.pump(const Duration(milliseconds: 20));
      expect(state.lastJobResult, isNotNull, reason: 'the reveal has started');
      expect(find.byKey(const Key('level-up-dialog')), findsNothing,
          reason: 'the dialog is the crescendo of the reveal, not a competitor to it');

      // Once the reveal has played out, Jobs shows it.
      await tester.pumpAndSettle(const Duration(milliseconds: 100), EnginePhase.sendSemanticsUpdate, const Duration(seconds: 10));
      expect(find.byKey(const Key('level-up-dialog')), findsOneWidget);
    });
  });

  group('ObjectivesScreen — one tap to the work', () {
    testWidgets('an unfinished job objective opens the Jobs screen', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('go-objective-corner-store-regular')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byType(JobsScreen), findsOneWidget);
    });

    testWidgets('an unfinished collection objective opens the Properties screen', (tester) async {
      final api = FakeApiClient(
        objectivesResult: ObjectiveList(
          date: '2026-10-13',
          resetsAtUtc: DateTime.utc(2026, 10, 14),
          objectives: const [kObjectiveProperty],
        ),
      );
      await pumpObjectives(tester, api);

      await tester.tap(find.byKey(const Key('go-objective-collect-rent')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byType(PropertiesScreen), findsOneWidget);
    });

    testWidgets('the button names where it goes', (tester) async {
      await pumpObjectives(tester, FakeApiClient());

      expect(find.text('Go to Jobs'), findsWidgets);
    });
  });

  group('ObjectivesScreen — states', () {
    testWidgets('shows a spinner until the first read lands', (tester) async {
      final api = FakeApiClient()..objectivesGate = Completer<void>();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(MaterialApp(home: ObjectivesScreen(appState: state)));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);

      api.objectivesGate!.complete();
      await tester.pump();
      await tester.pump();
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('a failed first read shows the error and a Retry that reads again', (tester) async {
      final api = FakeApiClient(throwOnObjectives: true);
      await pumpObjectives(tester, api);

      expect(find.byKey(const Key('objectives-error')), findsOneWidget);
      await tester.pump(const Duration(milliseconds: 10));
      final reads = api.objectivesCalls;

      await tester.tap(find.byKey(const Key('objectives-retry')));
      await tester.pump(const Duration(milliseconds: 10));

      expect(api.objectivesCalls, greaterThan(reads));
    });

    testWidgets('an empty day says so rather than showing a blank list', (tester) async {
      final api = FakeApiClient(
        objectivesResult: ObjectiveList(
          date: '2026-10-13',
          resetsAtUtc: DateTime.utc(2026, 10, 14),
          objectives: const [],
        ),
      );
      await pumpObjectives(tester, api);

      expect(find.byKey(const Key('objectives-empty')), findsOneWidget);
    });

    testWidgets('re-reads on its interval so the countdown and a new day stay current', (tester) async {
      final api = FakeApiClient();
      await pumpObjectives(tester, api, refreshInterval: const Duration(seconds: 30));
      final reads = api.objectivesCalls;

      await tester.pump(const Duration(seconds: 31));
      await tester.pump();

      expect(api.objectivesCalls, greaterThan(reads));
    });
  });

  group('ObjectivesScreen — accessibility', () {
    testWidgets('the bar is announced as one fact with its count, not by absorbing the card', (tester) async {
      final handle = tester.ensureSemantics();
      await pumpObjectives(tester, FakeApiClient());

      expect(find.bySemanticsLabel(RegExp('Corner Store Regular: 2 of 5')), findsWidgets);
      handle.dispose();
    });

    testWidgets('Claim and Go to carry the objective they belong to', (tester) async {
      final handle = tester.ensureSemantics();
      await pumpObjectives(tester, FakeApiClient());

      expect(find.bySemanticsLabel(RegExp('^Claim Two Different Jobs')), findsOneWidget);
      expect(find.bySemanticsLabel('Go to Jobs for Corner Store Regular'), findsOneWidget);
      handle.dispose();
    });
  });

  group('resetsInLabel', () {
    final resets = DateTime.utc(2026, 10, 14);

    test('hours and minutes', () {
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 13, 18, 48)), 'Resets in 5h 12m');
    });

    test('drops zero minutes', () {
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 13, 22)), 'Resets in 2h');
    });

    test('under an hour is minutes only', () {
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 13, 23, 18)), 'Resets in 42m');
    });

    test('rounds a part-minute up so it never claims a reset is nearer than it is', () {
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 13, 23, 59, 30)), 'Resets in 1m');
    });

    test('at or past the instant it says it is resetting', () {
      expect(resetsInLabel(resets, resets), 'Resetting now');
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 14, 0, 5)), 'Resetting now');
    });

    test('reads the instant in UTC whatever zone the clock is in', () {
      expect(resetsInLabel(resets, DateTime.utc(2026, 10, 13, 18, 48).toLocal()), 'Resets in 5h 12m');
    });
  });

  group('objectiveRewardLabel', () {
    test('leaves out whichever half is zero', () {
      expect(objectiveRewardLabel(kObjectiveOpen, 'Cash'), 'Cash 110 · 25 XP');
      expect(
        objectiveRewardLabel(
          DailyObjective(
            id: 'x', name: 'x', description: '', type: 'earnCash', target: 1,
            progress: 0, complete: false, claimed: false, rewardCash: 0, rewardXp: 40,
          ),
          'Cash',
        ),
        '40 XP',
      );
    });
  });
}
