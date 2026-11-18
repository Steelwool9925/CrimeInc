import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/screens/objectives_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> pumpHome(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  await tester.pumpWidget(MaterialApp(home: HomeScreen(appState: state, onChangeServer: () {})));
  await tester.pump();
  await tester.pump();
  return state;
}

String? subtitleOf(WidgetTester tester) {
  final tile = find.byKey(const Key('go-to-objectives'));
  final texts = tester.widgetList<Text>(find.descendant(of: tile, matching: find.byType(Text)));
  return texts.map((t) => t.data).whereType<String>().where((s) => s != 'Objectives').firstOrNull;
}

ObjectiveList listOf(List<DailyObjective> objectives) => ObjectiveList(
      date: '2026-10-13',
      resetsAtUtc: DateTime.utc(2026, 10, 14),
      objectives: objectives,
    );

void main() {
  group('Home — Objectives tile', () {
    testWidgets('is on the dashboard and opens the Objectives screen', (tester) async {
      await pumpHome(tester, FakeApiClient());

      expect(find.text('Objectives'), findsOneWidget);
      await tester.ensureVisible(find.byKey(const Key('go-to-objectives')));
      await tester.tap(find.byKey(const Key('go-to-objectives')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byType(ObjectivesScreen), findsOneWidget);
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('says how many are ready to claim, in words, and flags it with a dot', (tester) async {
      await pumpHome(tester, FakeApiClient());

      // The default list has one finished and unclaimed objective.
      expect(subtitleOf(tester), '1 ready to claim');
      expect(find.byKey(const Key('go-to-objectives-badge')), findsOneWidget);
    });

    testWidgets('with nothing ready, says how many are left today and shows no dot', (tester) async {
      await pumpHome(
        tester,
        FakeApiClient(objectivesResult: listOf(const [kObjectiveOpen, kObjectiveClaimed])),
      );

      expect(subtitleOf(tester), '1 left today');
      expect(find.byKey(const Key('go-to-objectives-badge')), findsNothing);
    });

    testWidgets('with everything claimed, says it is done', (tester) async {
      await pumpHome(tester, FakeApiClient(objectivesResult: listOf(const [kObjectiveClaimed])));

      expect(subtitleOf(tester), 'All done today');
      expect(find.byKey(const Key('go-to-objectives-badge')), findsNothing);
    });

    testWidgets('reports no count before the first read lands, rather than a wrong one', (tester) async {
      final api = FakeApiClient()..objectivesGate = Completer<void>();
      await pumpHome(tester, api);

      expect(subtitleOf(tester), isNull);
      expect(find.byKey(const Key('go-to-objectives-badge')), findsNothing);

      api.objectivesGate!.complete();
      await tester.pump();
    });

    testWidgets('an empty day reports no count', (tester) async {
      await pumpHome(tester, FakeApiClient(objectivesResult: listOf(const [])));

      expect(subtitleOf(tester), isNull);
    });

    testWidgets('claiming from the screen updates the tile behind it', (tester) async {
      final api = FakeApiClient();
      await pumpHome(tester, api);

      await tester.ensureVisible(find.byKey(const Key('go-to-objectives')));
      await tester.tap(find.byKey(const Key('go-to-objectives')));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));
      await tester.tap(find.byKey(const Key('claim-objective-two-different-jobs')));
      await tester.pump();
      await tester.pump();
      await tester.pageBack();
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(subtitleOf(tester), '1 left today');
      expect(find.byKey(const Key('go-to-objectives-badge')), findsNothing);
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('sits on the first screen of a phone, beside Overnight Jobs', (tester) async {
      tester.view.physicalSize = const Size(375, 667);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      // No upgrade to announce: that prompt takes a row of its own and is covered by the Home tests.
      await pumpHome(
        tester,
        FakeApiClient(
          inventoryResult: const InventoryList(items: [], loadout: kBareLoadout, cash: 12345, level: 1),
        ),
      );

      final objectives = tester.getRect(find.byKey(const Key('go-to-objectives')));
      final timedJobs = tester.getRect(find.byKey(const Key('go-to-timed-jobs')));

      expect(objectives.top, timedJobs.top, reason: 'second row, next to Overnight Jobs');
      expect(objectives.left, lessThan(timedJobs.left));
      // The bottom nav takes 56px of the 667. Like Overnight Jobs beside it, the tile must at least
      // begin on the first screen: the dot sits in its top corner.
      expect(objectives.top, lessThan(667.0 - 56.0),
          reason: 'the daily hook must not be hidden under the fold');
    });
  });
}
