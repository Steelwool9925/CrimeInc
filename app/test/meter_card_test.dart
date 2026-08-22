import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/meter_card.dart';

/// T4.2: the meter countdown ticks locally instead of standing still between polls.
///
/// The server sends secondsUntilNextPoint and AppState polls every ten seconds, so the old
/// display jumped ten at a time and read "+1 in 247s". A regen timer that does not visibly move
/// is the thing this task exists to fix.
void main() {
  MeterState meter({
    int current = 3,
    int max = 10,
    int regenSecondsPerPoint = 300,
    int? secondsUntilNextPoint = 65,
  }) =>
      MeterState(
        current: current,
        max: max,
        regenSecondsPerPoint: regenSecondsPerPoint,
        secondsUntilNextPoint: secondsUntilNextPoint,
      );

  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  group('formatCountdown', () {
    test('renders under a minute as plain seconds', () {
      expect(formatCountdown(45), '45s');
    });

    test('renders a whole minute as minutes and zero-padded seconds', () {
      expect(formatCountdown(60), '1m 00s');
    });

    test('zero-pads the seconds so the text does not jitter in width', () {
      expect(formatCountdown(65), '1m 05s');
    });

    test('renders several minutes', () {
      expect(formatCountdown(247), '4m 07s');
    });

    test('treats zero as due now', () {
      expect(formatCountdown(0), 'now');
    });

    test('never renders a negative duration', () {
      expect(formatCountdown(-5), 'now');
    });
  });

  group('MeterCard', () {
    testWidgets('shows the meter value and cap', (tester) async {
      await tester.pumpWidget(wrap(MeterCard(label: 'Energy', meter: meter())));

      expect(find.text('Energy'), findsOneWidget);
      expect(find.text('3 / 10'), findsOneWidget);
    });

    testWidgets('shows the time to the next point, formatted', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(label: 'Energy', meter: meter(secondsUntilNextPoint: 65))),
      );

      expect(find.text('+1 in 1m 05s'), findsOneWidget);
    });

    testWidgets('counts down as time passes', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(secondsUntilNextPoint: 65),
          tickInterval: const Duration(seconds: 1),
        )),
      );

      expect(find.text('+1 in 1m 05s'), findsOneWidget);

      await tester.pump(const Duration(seconds: 1));
      expect(find.text('+1 in 1m 04s'), findsOneWidget);

      await tester.pump(const Duration(seconds: 4));
      expect(find.text('+1 in 1m 00s'), findsOneWidget);

      // Let the timer settle so the test does not leave a pending timer.
      await tester.pumpWidget(const SizedBox());
    });

    testWidgets('holds at "now" rather than counting past zero', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(secondsUntilNextPoint: 2),
          tickInterval: const Duration(seconds: 1),
        )),
      );

      await tester.pump(const Duration(seconds: 5));

      expect(find.text('+1 in now'), findsNothing);
      expect(find.text('+1 due now'), findsOneWidget);

      await tester.pumpWidget(const SizedBox());
    });

    testWidgets('restarts from the server figure when a fresh poll arrives', (tester) async {
      // The local tick is an estimate between polls; the server is authoritative when it speaks.
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(secondsUntilNextPoint: 65),
          tickInterval: const Duration(seconds: 1),
        )),
      );
      await tester.pump(const Duration(seconds: 3));
      expect(find.text('+1 in 1m 02s'), findsOneWidget);

      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(secondsUntilNextPoint: 300),
          tickInterval: const Duration(seconds: 1),
        )),
      );
      await tester.pump();

      expect(find.text('+1 in 5m 00s'), findsOneWidget);

      await tester.pumpWidget(const SizedBox());
    });

    testWidgets('a full meter shows no countdown', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(current: 10, max: 10, secondsUntilNextPoint: null),
        )),
      );

      expect(find.textContaining('+1'), findsNothing);
      expect(find.text('Full'), findsOneWidget);
    });

    testWidgets('a meter the server sends no timing for shows no countdown', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(current: 3, max: 10, secondsUntilNextPoint: null),
        )),
      );

      expect(find.textContaining('+1'), findsNothing);
    });

    testWidgets('does not tick once the meter is full', (tester) async {
      await tester.pumpWidget(
        wrap(MeterCard(
          label: 'Energy',
          meter: meter(current: 10, max: 10, secondsUntilNextPoint: null),
          tickInterval: const Duration(seconds: 1),
        )),
      );

      await tester.pump(const Duration(seconds: 3));

      expect(find.text('Full'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
    });
  });
}
