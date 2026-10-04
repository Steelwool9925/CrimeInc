import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/meter_card.dart';

import 'fakes.dart';
import 'jobs_screen_test.dart' show pumpJobs;

/// Pins the things a player who cannot see the screen depends on.
///
/// The game renders to a canvas, so nothing here is implied by the widget tree the way it would
/// be in HTML — every accessible name is something the app has to say on purpose, and every one
/// of them can be dropped by a refactor without a single pixel changing. These tests exist so
/// that drop is a build failure.
///
/// Note the app itself calls `SemanticsBinding.instance.ensureSemantics()` in `main()` so the
/// tree is always built; `tester.ensureSemantics()` below is the test-harness equivalent, since
/// widget tests do not run `main()`.
void main() {
  group('Jobs list', () {
    testWidgets('every Do job button names the job it belongs to', (tester) async {
      // Without this the job list was six buttons all announcing the identical name "Do job",
      // in a list whose only distinguishing content sits in sibling nodes. There was no way to
      // tell which button ran which job, which makes the core loop unoperable rather than merely
      // awkward.
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, FakeApiClient());

      final label = tester
          .getSemantics(find.byKey(const Key('do-job-shake-down-corner-store')))
          .label;

      expect(label, contains('Shake Down a Corner Store'));
      semantics.dispose();
    });

    testWidgets('no two job buttons announce the same name', (tester) async {
      // The property that actually matters, stated directly rather than as a spot check: a
      // screen-reader user must be able to tell the buttons apart.
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, FakeApiClient());

      final labels = <String>[];
      for (final element in find.byType(ElevatedButton).evaluate()) {
        final node = tester.getSemantics(find.byElementPredicate((e) => e == element));
        if (node.label.contains('Do job')) labels.add(node.label);
      }

      expect(labels.length, greaterThan(1), reason: 'the fixture should offer several jobs');
      expect(labels.toSet().length, labels.length,
          reason: 'two job buttons announce the same name: $labels');
      semantics.dispose();
    });

    testWidgets('the mastery bar announces the rank, not the whole card', (tester) async {
      // An unlabelled progress indicator has no accessible name, so the engine absorbs nearby
      // text: this one announced itself as a progress bar named after the entire job card —
      // title, description, payout and odds — while never saying the rank it represents.
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, FakeApiClient());

      // Unanchored on purpose. How far the engine merges a card's nodes differs between the
      // widget-test tree (which folds the whole card into one node) and the web build (which
      // kept the bar as its own progressbar node — that is where this was spotted). What has to
      // hold either way is that the rank is spoken in words somewhere, rather than left implicit
      // in a bar.
      expect(find.bySemanticsLabel(RegExp(r'Mastery rank \d+ of \d+')), findsWidgets);
      // And the bar's own name is no longer the card's prose.
      expect(find.bySemanticsLabel(RegExp(r'^Lean on the owner')), findsNothing);
      semantics.dispose();
    });

    testWidgets('a refused job says why in its accessible name', (tester) async {
      // Sighted players get the reason from a line beside the button. That line is a separate
      // semantics node, and nothing tells a reader the two belong together — so the refusal has
      // to travel with the control it refuses.
      final semantics = tester.ensureSemantics();
      await pumpJobs(tester, FakeApiClient());

      final label = tester
          .getSemantics(find.byKey(const Key('do-job-rob-an-armored-transport')))
          .label;

      expect(label, contains('Rob an Armored Transport'));
      expect(label.toLowerCase(), contains('unavailable'));
      semantics.dispose();
    });
  });

  group('Meter card', () {
    Future<void> pumpMeter(WidgetTester tester, MeterState meter) async {
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: MeterCard(label: 'Energy', meter: meter)),
      ));
      await tester.pump();
    }

    testWidgets('a full meter reads as one sentence, not four fragments', (tester) async {
      // Visually a label, a count, a bar and a status line; to a reader those were four
      // unrelated nodes, with the progress bar announcing a bare percentage of its own.
      final semantics = tester.ensureSemantics();
      await pumpMeter(
          tester, const MeterState(current: 20, max: 20, regenSecondsPerPoint: 90));

      expect(find.bySemanticsLabel('Energy, 20 of 20, full'), findsOneWidget);
      // And the fragments it replaced are no longer announced separately.
      expect(find.bySemanticsLabel('20 / 20'), findsNothing);
      semantics.dispose();
    });

    testWidgets('a regenerating meter announces when the next point lands', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpMeter(
        tester,
        const MeterState(
          current: 7,
          max: 20,
          regenSecondsPerPoint: 90,
          secondsUntilNextPoint: 90,
        ),
      );

      // "20 / 20" is read as a date or a fraction depending on the reader, so the numbers are
      // spelled out rather than lifted from the visible glyphs.
      expect(find.bySemanticsLabel(RegExp(r'^Energy, 7 of 20, next point in ')), findsOneWidget);
      semantics.dispose();
    });
  });
}
