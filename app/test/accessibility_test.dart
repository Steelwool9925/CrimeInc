import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/meter_card.dart';

import 'fakes.dart';
import 'inventory_screen_test.dart' show pumpInventory;
import 'jobs_screen_test.dart' show pumpJobs;
import 'properties_screen_test.dart' show pumpProperties;
import 'store_screen_test.dart' show pumpStore;

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

  // The same defect the Jobs list had, on the other screens built the same way: a list of cards
  // each ending in a button whose visible label is just "Buy" or "Equip". A sighted player gets
  // the item from the card around it; a screen-reader user working from a list of buttons gets
  // nothing, so six (Properties) or nine (Market) identically named controls were
  // indistinguishable — and Buy is the one that spends cash.
  group('Properties', () {
    // A lazily built ListView only creates the cards it can see, and the default 800x600 test
    // surface shows two. Tall enough for all three fixture properties.
    void tallSurface(WidgetTester tester) {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
    }

    Future<List<String>> names(WidgetTester tester, List<String> keys) async {
      return [
        for (final k in keys) tester.getSemantics(find.byKey(Key(k))).label,
      ];
    }

    testWidgets('every Buy and Collect button names its property', (tester) async {
      tallSurface(tester);
      final semantics = tester.ensureSemantics();
      await pumpProperties(tester, FakeApiClient());

      final labels = await names(tester, const [
        'buy-property-street-corner',
        'buy-property-pawn-shop',
        'buy-property-car-wash',
        'collect-property-street-corner',
        'collect-property-pawn-shop',
      ]);

      expect(labels[0], 'Buy Street Corner');
      expect(labels[3], 'Collect from Street Corner');
      expect(labels.toSet().length, labels.length,
          reason: 'two property buttons announce the same name: $labels');
      semantics.dispose();
    });

    testWidgets('a refused button says why in its accessible name', (tester) async {
      // The reason sits in a separate text node that nothing ties to the control it refuses, so
      // it has to travel with the button's own name.
      tallSurface(tester);
      final semantics = tester.ensureSemantics();
      await pumpProperties(tester, FakeApiClient());

      final locked = tester.getSemantics(find.byKey(const Key('buy-property-car-wash'))).label;
      expect(locked, contains('Buy Car Wash'));
      expect(locked, contains('Unavailable: Requires level 4'));

      final empty =
          tester.getSemantics(find.byKey(const Key('collect-property-pawn-shop'))).label;
      expect(empty, contains('Collect from Pawn Shop'));
      expect(empty, contains('Unavailable: Nothing on the counter yet'));
      semantics.dispose();
    });

    testWidgets('the owned meter is one spoken fact, not a bare "30 / 60"', (tester) async {
      // An unlabelled progress bar absorbs the surrounding text as its name: on the live build the
      // owned card was exposed as a `progressbar` named after the entire card.
      final semantics = tester.ensureSemantics();
      await pumpProperties(tester, FakeApiClient());

      expect(find.bySemanticsLabel(RegExp(r'Banked 30 of 60 Cash')), findsWidgets);
      expect(find.bySemanticsLabel(RegExp(r'^30 / 60$')), findsNothing);
      semantics.dispose();
    });
  });

  group('Market and Inventory', () {
    testWidgets('no two Market buttons announce the same name', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpStore(tester, FakeApiClient());

      final labels = <String>[];
      for (final item in [kOwnedWeapon, kUnaffordableArmor, kLockedWeapon]) {
        labels.add(tester.getSemantics(find.byKey(Key('buy-item-${item.id}'))).label);
      }

      expect(labels, everyElement(startsWith('Buy ')));
      expect(labels.any((l) => l.contains(kOwnedWeapon.name)), isTrue);
      expect(labels.toSet().length, labels.length,
          reason: 'two Market buttons announce the same name: $labels');
      semantics.dispose();
    });

    testWidgets('the Market Equip button names the item it equips', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpStore(tester, FakeApiClient());

      final label =
          tester.getSemantics(find.byKey(Key('equip-item-${kOwnedWeapon.id}'))).label;
      expect(label, 'Equip ${kOwnedWeapon.name}');
      semantics.dispose();
    });

    testWidgets('the Inventory Equip button names the item it equips', (tester) async {
      final semantics = tester.ensureSemantics();
      await pumpInventory(tester, FakeApiClient());

      final label =
          tester.getSemantics(find.byKey(Key('equip-item-${kOwnedWeapon.id}'))).label;
      expect(label, 'Equip ${kOwnedWeapon.name}');
      semantics.dispose();
    });
  });
}
