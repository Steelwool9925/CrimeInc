import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Me _meWith({int streak = 0, bool claimable = false}) => Me(
      playerId: kFakeMe.playerId,
      energy: kFakeMe.energy,
      stamina: kFakeMe.stamina,
      health: kFakeMe.health,
      cash: kFakeMe.cash,
      premiumCurrency: kFakeMe.premiumCurrency,
      level: kFakeMe.level,
      xp: kFakeMe.xp,
      skillPoints: kFakeMe.skillPoints,
      attack: kFakeMe.attack,
      defense: kFakeMe.defense,
      dailyStreakCurrent: streak,
      dailyClaimable: claimable,
    );

Widget _wrap(AppState state) => MaterialApp(
      home: HomeScreen(appState: state, onChangeServer: () {}),
    );

Future<AppState> _pumpHome(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(_wrap(state));
  await tester.pump();
  await tester.pump();
  return state;
}

void main() {
  group('HomeScreen — streak indicator', () {
    testWidgets('shows the current streak once the player has claimed before', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(streak: 3)));

      expect(find.byKey(const Key('home-streak')), findsOneWidget);
      expect(find.text('Day 3 streak'), findsOneWidget);
    });

    testWidgets('is absent for a player who has never claimed', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(streak: 0)));

      expect(find.byKey(const Key('home-streak')), findsNothing);
    });
  });

  group('HomeScreen — daily reward dialog auto-show', () {
    testWidgets('shows on arrival when a claim is waiting', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(claimable: true)));

      expect(find.byKey(const Key('daily-reward-dialog')), findsOneWidget);
    });

    testWidgets('does not show when nothing is waiting', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(claimable: false)));

      expect(find.byKey(const Key('daily-reward-dialog')), findsNothing);
    });

    testWidgets('cannot be dismissed by tapping outside', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(claimable: true)));
      expect(find.byKey(const Key('daily-reward-dialog')), findsOneWidget);

      await tester.tapAt(const Offset(2, 2));
      await tester.pump();

      expect(find.byKey(const Key('daily-reward-dialog')), findsOneWidget);
    });
  });

  group('DailyRewardDialog — claiming', () {
    testWidgets('starts with a generic prompt and an enabled Claim button', (tester) async {
      await _pumpHome(tester, FakeApiClient(meResult: _meWith(claimable: true)));

      expect(find.text('Come back every day for a bigger reward.'), findsOneWidget);
      expect(find.byKey(const Key('daily-reward-cash')), findsNothing);
      final button = tester.widget<FilledButton>(find.byKey(const Key('daily-reward-claim')));
      expect(button.onPressed, isNotNull);
      expect(find.text('Claim'), findsOneWidget);
    });

    testWidgets('tapping Claim shows the server\'s reward lines, never a local guess', (tester) async {
      final api = FakeApiClient(
        meResult: _meWith(claimable: true),
        claimDailyResult: ClaimDailyResult(
          reward: const DailyReward(day: 3, cash: 100, xp: 20),
          currentStreak: 3,
          longestStreak: 3,
          player: _meWith(streak: 3, claimable: false),
        ),
      );
      final state = await _pumpHome(tester, api);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();
      await tester.pump();

      expect(api.claimDailyCalls, 1);
      expect(find.text('+\$100'), findsOneWidget);
      expect(find.text('+20 XP'), findsOneWidget);
      expect(find.byKey(const Key('daily-reward-energy')), findsOneWidget);
      expect(find.text('Longest streak yet!'), findsOneWidget);
      expect(find.text('Nice'), findsOneWidget);
      expect(state.me!.dailyStreakCurrent, 3);
      expect(state.me!.dailyClaimable, isFalse);
    });

    testWidgets('no "longest streak" nod when this is not a new record', (tester) async {
      final api = FakeApiClient(
        meResult: _meWith(claimable: true),
        claimDailyResult: ClaimDailyResult(
          reward: const DailyReward(day: 2, cash: 75, xp: 15),
          currentStreak: 2,
          longestStreak: 5,
          player: _meWith(streak: 2, claimable: false),
        ),
      );
      await _pumpHome(tester, api);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();
      await tester.pump();

      expect(find.text('Longest streak yet!'), findsNothing);
    });

    testWidgets('tapping Nice closes the dialog and clears the result', (tester) async {
      final api = FakeApiClient(meResult: _meWith(claimable: true));
      final state = await _pumpHome(tester, api);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();
      await tester.pump();
      expect(find.text('Nice'), findsOneWidget);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byKey(const Key('daily-reward-dialog')), findsNothing);
      expect(state.lastDailyClaim, isNull);
    });

    testWidgets('a refusal is shown inline and the dialog stays open to retry', (tester) async {
      final api = FakeApiClient(
        meResult: _meWith(claimable: true),
        refuseClaimDailyWith: 'alreadyClaimedToday',
      );
      await _pumpHome(tester, api);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('daily-reward-dialog')), findsOneWidget);
      expect(find.text('You already claimed today\'s reward.'), findsOneWidget);
      final button = tester.widget<FilledButton>(find.byKey(const Key('daily-reward-claim')));
      expect(button.onPressed, isNotNull);
    });

    testWidgets('while claiming is in flight the button is disabled', (tester) async {
      final api = FakeApiClient(meResult: _meWith(claimable: true))
        ..claimDailyGate = Completer<void>();
      await _pumpHome(tester, api);

      await tester.tap(find.byKey(const Key('daily-reward-claim')));
      await tester.pump();

      final button = tester.widget<FilledButton>(find.byKey(const Key('daily-reward-claim')));
      expect(button.onPressed, isNull);

      api.claimDailyGate!.complete();
      await tester.pump();
      await tester.pump();
    });
  });
}
