import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/widgets/job_reveal.dart';

const _duration = Duration(milliseconds: 400);

/// Pumps a [JobReveal] and records every progress value its builder is given.
class _Probe {
  final List<double> seen = [];
  final List<Object> revealed = [];

  double get last => seen.last;

  Widget build(Object? subject, {Duration duration = _duration}) => MaterialApp(
        home: JobReveal<Object>(
          subject: subject,
          duration: duration,
          onRevealed: revealed.add,
          builder: (context, t) {
            seen.add(t);
            return const SizedBox();
          },
        ),
      );
}

void main() {
  group('JobReveal', () {
    testWidgets('rests at zero and says nothing while there is no subject', (tester) async {
      final probe = _Probe();

      await tester.pumpWidget(probe.build(null));
      await tester.pump(const Duration(seconds: 1));

      expect(probe.seen.every((t) => t == 0), isTrue);
      expect(probe.revealed, isEmpty);
    });

    testWidgets('runs from 0 to 1 over its duration and announces once, at the end',
        (tester) async {
      final probe = _Probe();
      final subject = Object();

      await tester.pumpWidget(probe.build(subject));
      expect(probe.last, 0);

      await tester.pump(const Duration(milliseconds: 200));
      expect(probe.last, closeTo(0.5, 0.05));
      expect(probe.revealed, isEmpty);

      await tester.pump(const Duration(milliseconds: 250));
      expect(probe.last, 1);
      expect(probe.revealed, [subject]);

      await tester.pump(const Duration(seconds: 1));
      expect(probe.revealed, hasLength(1));
    });

    testWidgets('progress never goes backwards within one reveal', (tester) async {
      final probe = _Probe();

      await tester.pumpWidget(probe.build(Object()));
      for (var i = 0; i < 30; i++) {
        await tester.pump(const Duration(milliseconds: 16));
      }

      for (var i = 1; i < probe.seen.length; i++) {
        expect(probe.seen[i], greaterThanOrEqualTo(probe.seen[i - 1]));
      }
    });

    testWidgets('the same subject rebuilt does not restart the reveal', (tester) async {
      final probe = _Probe();
      final subject = Object();

      await tester.pumpWidget(probe.build(subject));
      await tester.pump(const Duration(milliseconds: 300));
      final before = probe.last;

      await tester.pumpWidget(probe.build(subject));
      expect(probe.last, greaterThanOrEqualTo(before));
      await tester.pumpAndSettle();
      expect(probe.revealed, [subject]);
    });

    testWidgets('a new subject restarts from zero, and the interrupted one never announces',
        (tester) async {
      final probe = _Probe();
      final first = Object();
      final second = Object();

      await tester.pumpWidget(probe.build(first));
      await tester.pump(const Duration(milliseconds: 200));
      await tester.pumpWidget(probe.build(second));
      expect(probe.last, 0);

      await tester.pumpAndSettle();
      expect(probe.revealed, [second]);
    });

    testWidgets('clearing the subject rests it at zero, and the same one can reveal again',
        (tester) async {
      final probe = _Probe();
      final subject = Object();

      await tester.pumpWidget(probe.build(subject));
      await tester.pumpAndSettle();
      await tester.pumpWidget(probe.build(null));
      expect(probe.last, 0);

      await tester.pumpWidget(probe.build(subject));
      await tester.pumpAndSettle();
      expect(probe.revealed, [subject, subject]);
    });

    testWidgets('tearing it down mid-reveal leaves no ticker or error behind', (tester) async {
      final probe = _Probe();

      await tester.pumpWidget(probe.build(Object()));
      await tester.pump(const Duration(milliseconds: 100));
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump(const Duration(seconds: 1));

      expect(tester.takeException(), isNull);
      expect(probe.revealed, isEmpty);
    });

    group('with animations disabled in the OS', () {
      void disableAnimations(WidgetTester tester) {
        tester.platformDispatcher.accessibilityFeaturesTestValue =
            const FakeAccessibilityFeatures(disableAnimations: true);
        addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
      }

      testWidgets('the first frame is already the final one, and nothing animates', (tester) async {
        disableAnimations(tester);
        final probe = _Probe();
        final subject = Object();

        await tester.pumpWidget(probe.build(subject));

        expect(probe.seen, everyElement(1.0));
        expect(tester.hasRunningAnimations, isFalse);

        await tester.pump();
        expect(probe.revealed, [subject]);
      });

      testWidgets('no subject is still zero, never a finished reveal', (tester) async {
        disableAnimations(tester);
        final probe = _Probe();

        await tester.pumpWidget(probe.build(null));

        expect(probe.seen, everyElement(0.0));
        expect(probe.revealed, isEmpty);
      });

      testWidgets('turning it on mid-reveal jumps to the final frame and announces',
          (tester) async {
        final probe = _Probe();
        final subject = Object();

        await tester.pumpWidget(probe.build(subject));
        await tester.pump(const Duration(milliseconds: 100));
        expect(probe.last, lessThan(1));

        tester.platformDispatcher.accessibilityFeaturesTestValue =
            const FakeAccessibilityFeatures(disableAnimations: true);
        addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
        await tester.pump();
        await tester.pump();

        expect(probe.last, 1);
        expect(probe.revealed, [subject]);
      });
    });
  });
}
