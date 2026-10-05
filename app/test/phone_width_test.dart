import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';

import 'fakes.dart';
import 'jobs_screen_test.dart' show pumpJobs;
import 'properties_screen_test.dart' show pumpProperties;

/// The screens that carry the most text, at the width of a small phone.
///
/// A browser-driven pass cannot reach this: resizing the window in that harness reports success and
/// the page does not reflow, so phone layout was previously listed as unverified. A widget test can
/// set the surface to 360 logical pixels, and Flutter turns any overflowing row into a test failure,
/// which is exactly the defect a narrow screen produces.
void main() {
  Future<void> narrow(WidgetTester tester) async {
    tester.view.physicalSize = const Size(360, 780);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpAndSettle();
  }

  group('Jobs at 360 wide', () {
    testWidgets('an item job with a stepper and a long reason lays out without overflowing',
        (tester) async {
      await pumpJobs(
        tester,
        FakeApiClient(
          jobsResult: const JobList(jobs: [kExtrasJob, kItemGatedJob], currentEnergy: 20, level: 3),
        ),
      );
      await narrow(tester);

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('job-extras-label-hijack-a-delivery-truck-switchblade')), findsOneWidget);
    });

    testWidgets('the result banner with a used-up tile lays out without overflowing', (tester) async {
      await pumpJobs(
        tester,
        FakeApiClient(
          jobsResult: const JobList(jobs: [kExtrasJob], currentEnergy: 20, level: 3),
          doJobResult: const DoJobResult(result: kItemConsumingJobResult, player: kFakeMe),
        ),
      );
      await narrow(tester);
      await tester.tap(find.byKey(const Key('do-job-hijack-a-delivery-truck')));
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('job-result-item-switchblade')), findsOneWidget);
    });

    testWidgets('the empty placeholder panel lays out without overflowing', (tester) async {
      await pumpJobs(tester, FakeApiClient());
      await narrow(tester);

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('job-result-placeholder')), findsOneWidget);
    });
  });

  group('Properties at 360 wide', () {
    Property owned() => const Property(
          id: 'street-corner',
          name: 'Street Corner',
          description: 'A patch of pavement nobody works without your say-so.',
          levelRequirement: 1,
          price: 500,
          incomePerHour: 240,
          accrualCapMinutes: 480,
          quantity: 3,
          maxUnits: 3,
          pending: 0,
          cap: 5760,
          canBuy: false,
          buyUnavailableReason: 'limit',
          canCollect: false,
          collectUnavailableReason: 'nothingToCollect',
        );

    testWidgets('an owned card at its limit, refused on both counts, lays out without overflowing',
        (tester) async {
      await pumpProperties(
        tester,
        FakeApiClient(propertiesResult: PropertyList(properties: [owned()], cash: 0, level: 12)),
      );
      await narrow(tester);

      expect(tester.takeException(), isNull);
      expect(find.text('Buy: You hold the most you can'), findsOneWidget);
      expect(find.byKey(const Key('property-units-street-corner')), findsOneWidget);
    });

    testWidgets('the whole default catalogue lays out without overflowing', (tester) async {
      await pumpProperties(tester, FakeApiClient());
      await narrow(tester);

      expect(tester.takeException(), isNull);
    });
  });
}
