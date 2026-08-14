import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Widget _wrap(AppState state) => MaterialApp(
      home: HomeScreen(appState: state),
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

      // Energy has secondsUntilNextPoint: 42
      expect(find.text('+1 in 42s'), findsOneWidget);
      // Stamina has secondsUntilNextPoint: 120
      expect(find.text('+1 in 120s'), findsOneWidget);
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
}
