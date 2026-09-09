import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Widget _wrap(AppState state, {VoidCallback? onChangeServer}) => MaterialApp(
      home: HomeScreen(appState: state, onChangeServer: onChangeServer ?? () {}),
    );

AppState _readyState() {
  final api = FakeApiClient();
  final store = InMemoryTokenStore();
  return AppState(apiClient: api, tokenStore: store);
}

Future<AppState> _bootstrapped(WidgetTester tester) async {
  final state = _readyState();
  await state.bootstrap();
  return state;
}

void main() {
  group('HomeScreen — loading state', () {
    testWidgets('shows CircularProgressIndicator while loading',
        (tester) async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      // Do NOT bootstrap — stays in loading

      await tester.pumpWidget(_wrap(state));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });
  });

  group('HomeScreen — error state', () {
    testWidgets('shows error message and Retry button', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.textContaining('error'), findsWidgets);
      expect(find.widgetWithText(ElevatedButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows a Change Server button next to Retry', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // A stuck server address (e.g. a friend's tunnel URL that has since rotated, per
      // docs/hosting-a-friend.md) is otherwise a dead end: Retry can only ever hit the same
      // unreachable host again, with no in-app way back to ServerAddressScreen.
      expect(find.widgetWithText(OutlinedButton, 'Change Server'), findsOneWidget);
    });

    testWidgets('Change Server button calls onChangeServer', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      var called = false;
      await tester.pumpWidget(_wrap(state, onChangeServer: () => called = true));
      await tester.pump();

      await tester.tap(find.widgetWithText(OutlinedButton, 'Change Server'));
      await tester.pump();

      expect(called, isTrue);
    });
  });

  group('HomeScreen — ready state', () {
    testWidgets('shows cash value and label', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // Cash label
      expect(find.text('Cash'), findsOneWidget);
      // Cash value — 12345 from FakeApiClient default
      expect(find.text('12345'), findsOneWidget);
    });

    testWidgets('shows energy meter with 7 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Energy'), findsOneWidget);
      expect(find.text('7 / 10'), findsOneWidget);
    });

    testWidgets('shows stamina meter with 5 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Stamina'), findsOneWidget);
      expect(find.text('5 / 10'), findsOneWidget);
    });

    testWidgets('shows health meter with 10 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Health'), findsOneWidget);
      expect(find.text('10 / 10'), findsOneWidget);
    });

    testWidgets('shows three LinearProgressIndicators', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.byType(LinearProgressIndicator), findsNWidgets(3));
    });

    testWidgets('shows secondsUntilNextPoint for non-full meters',
        (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // Energy has secondsUntilNextPoint: 42 — under a minute, so plain seconds.
      expect(find.text('+1 in 42s'), findsOneWidget);
      // Stamina has secondsUntilNextPoint: 120. T4.2 formats anything over a minute as
      // minutes and zero-padded seconds; this read "+1 in 120s" before.
      expect(find.text('+1 in 2m 00s'), findsOneWidget);
      // Health is full — no countdown
      expect(find.textContaining('in '), findsNWidgets(2));
    });

    testWidgets('full meter does not show countdown text', (tester) async {
      final api = FakeApiClient(
        meResult: const Me(
          playerId: 'p1',
          energy: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          stamina: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          health: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          cash: 0,
          premiumCurrency: 0,
          level: 1,
          xp: 0,
          skillPoints: 0,
          attack: 10,
          defense: 10,
        ),
      );
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.textContaining('+1 in'), findsNothing);
    });

    testWidgets('Retry button triggers bootstrap again', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.widgetWithText(ElevatedButton, 'Retry'), findsOneWidget);
      await tester.tap(find.widgetWithText(ElevatedButton, 'Retry'));
      await tester.pump();

      expect(api.sessionCalls.length, greaterThan(1));
    });
  });

  group('HomeScreen — navigation', () {
    testWidgets('offers a way into the Store', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      // Without this the screen exists but is unreachable, which is the same as not shipping
      // it: CP3 is a sequence a player performs, not a set of routes.
      expect(find.byKey(const Key('go-to-store')), findsOneWidget);
    });

    testWidgets('the Store button opens the Store', (tester) async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      // Home scrolls: with four destinations the later buttons sit below the fold on a small
      // viewport, so reaching them is a scroll the player makes too.
      await tester.ensureVisible(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('store-card-brass-knuckles')), findsOneWidget);
    });

    testWidgets('offers a way into Properties', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('go-to-properties')), findsOneWidget);
    });

    testWidgets('the Properties button opens Properties', (tester) async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-properties')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-properties')));

      // Not pumpAndSettle past this point: the Properties screen keeps a ticker running to draw
      // its counters filling, so there is no frame after which nothing is scheduled.
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('property-card-street-corner')), findsOneWidget);

      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('the Crew tile opens the Crew coming-soon screen', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-crew')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-crew')));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'Crew'), findsOneWidget);
    });

    testWidgets('the Territory tile opens the Territory coming-soon screen',
        (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-territory')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-territory')));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'Territory'), findsOneWidget);
    });

    testWidgets('the Rap Sheet tile opens the Profile screen', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-profile')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-profile')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('profile-stat-cash')), findsOneWidget);
    });
  });
}
