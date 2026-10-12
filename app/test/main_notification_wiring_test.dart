import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:underworld_empire/main.dart';

import 'fakes.dart';

/// `main.dart` is where return notifications are actually attached: the real preference store, the
/// lifecycle wrapper, and the property read at sign-in. Everything else is tested against pieces;
/// this runs the pieces together, from a cold start.
void main() {
  const signedIn = {
    'server_address': 'http://localhost:5080',
    'device_token': 'token',
  };
  const offer = Key('notification-offer');
  const yes = Key('notification-offer-yes');
  const doJob = Key('do-job-shake-down-corner-store');

  Future<void> launch(
    WidgetTester tester, {
    required FakeApiClient api,
    required RecordingReturnNotifier notifier,
  }) async {
    tester.view.physicalSize = const Size(400, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(UnderworldEmpireApp(
      key: UniqueKey(),
      apiClientFactory: (_) => api,
      returnNotifier: notifier,
    ));
    await tester.pumpAndSettle();
    // Tear the tree down inside the test's own zone so the Jobs screen's refresh timer is gone
    // before the binding checks for timers still pending.
    addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  }

  // "Jobs" is also a tile on Home, so the tap is scoped to the bottom bar.
  Future<void> openJobs(WidgetTester tester) async {
    await tester.tap(find.descendant(
      of: find.byType(BottomNavigationBar),
      matching: find.text('Jobs'),
    ));
    await tester.pumpAndSettle();
  }

  Future<void> openJobsAndDoOne(WidgetTester tester) async {
    await openJobs(tester);
    await tester.tap(find.byKey(doJob));
    await tester.pumpAndSettle();
  }

  testWidgets('a cold start asks nothing, then the first job offers, yes asks the OS once',
      (tester) async {
    SharedPreferences.setMockInitialValues(signedIn);
    final notifier = RecordingReturnNotifier();
    await launch(tester, api: FakeApiClient(), notifier: notifier);
    expect(notifier.permissionRequests, 0, reason: 'nothing is asked at launch');

    await openJobs(tester);
    expect(find.byKey(offer), findsNothing);

    await tester.tap(find.byKey(doJob));
    await tester.pumpAndSettle();
    expect(find.byKey(offer), findsOneWidget);
    expect(notifier.permissionRequests, 0, reason: 'an offer is not an OS prompt');

    await tester.tap(find.byKey(yes));
    await tester.pumpAndSettle();
    expect(notifier.permissionRequests, 1);
    expect(find.byKey(offer), findsNothing);
  });

  testWidgets('backgrounding schedules the energy nudge and coming back clears it',
      (tester) async {
    SharedPreferences.setMockInitialValues(signedIn);
    final notifier = RecordingReturnNotifier();
    await launch(tester, api: FakeApiClient(), notifier: notifier);
    await openJobsAndDoOne(tester);
    await tester.tap(find.byKey(yes));
    await tester.pumpAndSettle();

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();

    expect(notifier.replacements, hasLength(1));
    expect(notifier.lastScheduled!.map((n) => n.key), contains('energy'));
    expect(notifier.cancelCalls, 0);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pump();

    expect(notifier.cancelCalls, 1);
  });

  testWidgets('the choice survives a restart through the real store', (tester) async {
    SharedPreferences.setMockInitialValues(signedIn);
    await launch(tester, api: FakeApiClient(), notifier: RecordingReturnNotifier());
    await openJobsAndDoOne(tester);
    await tester.tap(find.byKey(yes));
    await tester.pumpAndSettle();

    // The next launch: a new app and a new notifier, but the same stored preferences.
    await tester.pumpWidget(const SizedBox.shrink());
    final secondLaunch = RecordingReturnNotifier();
    await launch(tester, api: FakeApiClient(), notifier: secondLaunch);
    await openJobsAndDoOne(tester);

    expect(find.byKey(offer), findsNothing, reason: 'a player who said yes is never offered again');
    expect(secondLaunch.permissionRequests, 0);
  });

  testWidgets('not now leaves the choice unasked, so the next launch offers again',
      (tester) async {
    SharedPreferences.setMockInitialValues(signedIn);
    await launch(tester, api: FakeApiClient(), notifier: RecordingReturnNotifier());
    await openJobsAndDoOne(tester);
    await tester.tap(find.byKey(const Key('notification-offer-later')));
    await tester.pumpAndSettle();

    await tester.pumpWidget(const SizedBox.shrink());
    final secondLaunch = RecordingReturnNotifier();
    await launch(tester, api: FakeApiClient(), notifier: secondLaunch);
    await openJobsAndDoOne(tester);

    expect(find.byKey(offer), findsOneWidget);
    expect(secondLaunch.permissionRequests, 0);
  });

  testWidgets('an opted-in player has the properties read at startup; an undecided one does not',
      (tester) async {
    SharedPreferences.setMockInitialValues({
      ...signedIn,
      'return_notifications_enabled': true,
    });
    final optedIn = FakeApiClient();
    await launch(tester, api: optedIn, notifier: RecordingReturnNotifier());
    expect(optedIn.propertiesCalls, 1);

    await tester.pumpWidget(const SizedBox.shrink());
    SharedPreferences.setMockInitialValues(signedIn);
    final undecided = FakeApiClient();
    await launch(tester, api: undecided, notifier: RecordingReturnNotifier());
    expect(undecided.propertiesCalls, 0,
        reason: 'nobody will be told about the takings, so they are not fetched');
  });

  testWidgets('a player who opted out schedules nothing when backgrounded', (tester) async {
    SharedPreferences.setMockInitialValues({
      ...signedIn,
      'return_notifications_enabled': false,
    });
    final notifier = RecordingReturnNotifier();
    await launch(tester, api: FakeApiClient(), notifier: notifier);

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();

    expect(notifier.lastScheduled, isEmpty);
  });

  testWidgets('a permission revoked while the app was closed shows as off and blocked',
      (tester) async {
    SharedPreferences.setMockInitialValues({
      ...signedIn,
      'return_notifications_enabled': true,
    });
    await launch(
      tester,
      api: FakeApiClient(),
      notifier: RecordingReturnNotifier(permitted: false),
    );

    await tester.tap(find.descendant(
      of: find.byType(BottomNavigationBar),
      matching: find.text('Profile'),
    ));
    await tester.pumpAndSettle();
    final toggle = find.byKey(const Key('profile-notifications-toggle'));
    await tester.ensureVisible(toggle);

    expect(tester.widget<SwitchListTile>(toggle).value, isFalse,
        reason: 'a switch that reads on for reminders that cannot arrive is a false promise');
    expect(find.byKey(const Key('profile-notifications-blocked')), findsOneWidget);
  });
}
