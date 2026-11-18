import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/timed_jobs_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> pumpTimedJobs(
  WidgetTester tester,
  FakeApiClient api, {
  Duration refreshInterval = const Duration(hours: 1),
}) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(
    home: TimedJobsScreen(appState: state, refreshInterval: refreshInterval),
  ));
  await tester.pump();
  await tester.pump();

  // Cancel the screen's refresh timer inside the test's own zone.
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  return state;
}

void main() {
  group('TimedJobsScreen — list', () {
    testWidgets('draws a card per catalog entry, with its name, duration and details', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('timed-job-card-run-a-late-night-shipment')), findsOneWidget);
      expect(find.byKey(const Key('timed-job-card-launder-a-nights-takings')), findsOneWidget);
      expect(find.text('Run a Late-Night Shipment'), findsOneWidget);
      expect(find.text('8h'), findsOneWidget);
      expect(find.text('24h'), findsOneWidget);
      expect(find.byKey(const Key('timed-job-details-run-a-late-night-shipment')), findsOneWidget);
    });

    testWidgets('a startable entry details label joins energy, cash range and XP', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient());

      final details = tester.widget<Text>(
          find.byKey(const Key('timed-job-details-run-a-late-night-shipment')));
      expect(details.data, 'Energy 3 · Cash 180-320 · 40 XP');
    });

    testWidgets('a gated entry\'s Start button is disabled and shows its lock reason', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient());

      final button = tester.widget<OutlinedButton>(
          find.byKey(const Key('start-timed-job-launder-a-nights-takings')));
      expect(button.onPressed, isNull);
      expect(find.text('Level 3 required'), findsOneWidget);
    });

    testWidgets('a startable entry offers an enabled Start button', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient());

      final button = tester.widget<FilledButton>(
          find.byKey(const Key('start-timed-job-run-a-late-night-shipment')));
      expect(button.onPressed, isNotNull);
    });

    testWidgets('with nothing running, no running card or locked note is shown', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient());

      expect(find.byKey(const Key('timed-job-running')), findsNothing);
      expect(find.byKey(const Key('timed-jobs-locked-note')), findsNothing);
    });
  });

  group('TimedJobsScreen — starting', () {
    testWidgets('tapping an enabled Start calls the API and shows the running card', (tester) async {
      final api = FakeApiClient();
      await pumpTimedJobs(tester, api);

      await tester.tap(find.byKey(const Key('start-timed-job-run-a-late-night-shipment')));
      await tester.pump();
      await tester.pump();

      expect(api.startTimedJobCalls, ['run-a-late-night-shipment']);
      expect(find.byKey(const Key('timed-job-running')), findsOneWidget);
      expect(find.byKey(const Key('timed-jobs-locked-note')), findsOneWidget);
    });

    testWidgets('a refusal shows the translated action-message banner', (tester) async {
      await pumpTimedJobs(tester, FakeApiClient(refuseStartTimedJobWith: 'level'));

      await tester.tap(find.byKey(const Key('start-timed-job-run-a-late-night-shipment')));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.text('Your level is too low for that appointment.'), findsOneWidget);
    });
  });

  group('TimedJobsScreen — collecting', () {
    TimedJobList runningList({required int remainingSeconds, required bool ready}) => TimedJobList(
          timedJobs: kTimedJobList.timedJobs,
          running: RunningTimedJob(
            timedJobId: 'run-a-late-night-shipment',
            name: 'Run a Late-Night Shipment',
            startedAtUtc: DateTime.utc(2026, 11, 17, 12, 0),
            remainingSeconds: remainingSeconds,
            ready: ready,
          ),
          currentEnergy: 4,
          level: 1,
        );

    testWidgets('shows "Not ready yet" and a disabled Collect before the countdown reaches zero',
        (tester) async {
      final api = FakeApiClient(timedJobsResult: runningList(remainingSeconds: 2, ready: false));
      await pumpTimedJobs(tester, api);

      final button =
          tester.widget<FilledButton>(find.byKey(const Key('collect-timed-job')));
      expect(button.onPressed, isNull);
      expect(find.text('Not ready yet'), findsOneWidget);
    });

    testWidgets('becomes ready and offers Collect once the local countdown hits zero', (tester) async {
      final api = FakeApiClient(timedJobsResult: runningList(remainingSeconds: 2, ready: false));
      await pumpTimedJobs(tester, api);

      await tester.pump(const Duration(seconds: 1));
      await tester.pump(const Duration(seconds: 1));

      expect(find.text('Collect'), findsOneWidget);
      final button =
          tester.widget<FilledButton>(find.byKey(const Key('collect-timed-job')));
      expect(button.onPressed, isNotNull);
    });

    testWidgets('tapping Collect calls the API and the catalog becomes startable again', (tester) async {
      final api = FakeApiClient(timedJobsResult: runningList(remainingSeconds: 1, ready: false));
      await pumpTimedJobs(tester, api);

      await tester.pump(const Duration(seconds: 1));
      await tester.tap(find.byKey(const Key('collect-timed-job')));
      await tester.pump();
      await tester.pump();

      expect(api.collectTimedJobCalls, 1);
      expect(find.byKey(const Key('timed-job-running')), findsNothing);
      final button = tester.widget<FilledButton>(
          find.byKey(const Key('start-timed-job-run-a-late-night-shipment')));
      expect(button.onPressed, isNotNull);
    });

    testWidgets('a collect refusal shows the translated action-message banner', (tester) async {
      final api = FakeApiClient(
        timedJobsResult: runningList(remainingSeconds: 1, ready: false),
        refuseCollectTimedJobWith: 'notReady',
      );
      await pumpTimedJobs(tester, api);

      await tester.pump(const Duration(seconds: 1));
      await tester.tap(find.byKey(const Key('collect-timed-job')));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.text('Nothing is ready to collect.'), findsOneWidget);
    });
  });

  group('TimedJobsScreen — states', () {
    testWidgets('shows a spinner until the first read lands', (tester) async {
      final api = FakeApiClient()..timedJobsGate = Completer<void>();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(MaterialApp(home: TimedJobsScreen(appState: state)));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);

      api.timedJobsGate!.complete();
      await tester.pump();
      await tester.pump();
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('a failed first read shows the error and a Retry that reads again', (tester) async {
      final api = FakeApiClient(throwOnTimedJobs: true);
      await pumpTimedJobs(tester, api);

      expect(find.byKey(const Key('timed-jobs-error')), findsOneWidget);
      await tester.pump(const Duration(milliseconds: 10));
      final reads = api.timedJobsCalls;

      await tester.tap(find.byKey(const Key('timed-jobs-retry')));
      await tester.pump(const Duration(milliseconds: 10));

      expect(api.timedJobsCalls, greaterThan(reads));
    });

    testWidgets('re-reads on its interval', (tester) async {
      final api = FakeApiClient();
      await pumpTimedJobs(tester, api, refreshInterval: const Duration(seconds: 30));
      final reads = api.timedJobsCalls;

      await tester.pump(const Duration(seconds: 31));
      await tester.pump();

      expect(api.timedJobsCalls, greaterThan(reads));
    });
  });

  group('timedJobDurationLabel', () {
    test('a whole number of hours', () {
      expect(timedJobDurationLabel(28800), '8h');
      expect(timedJobDurationLabel(86400), '24h');
    });

    test('drops zero minutes but keeps a part-hour remainder', () {
      expect(timedJobDurationLabel(5400), '1h 30m');
    });
  });

  group('timedJobDetailsLabel', () {
    test('drops XP when zero', () {
      expect(
        timedJobDetailsLabel(
          const TimedJob(
            id: 'x',
            name: 'x',
            description: '',
            durationSeconds: 3600,
            energyCost: 1,
            levelRequirement: 1,
            cashMin: 10,
            cashMax: 10,
            xp: 0,
            canStart: true,
          ),
          'Cash',
        ),
        'Energy 1 · Cash 10',
      );
    });
  });
}
