import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/notifications/return_lifecycle.dart';
import 'package:underworld_empire/notifications/return_notifier.dart';
import 'package:underworld_empire/notifications/return_schedule.dart';
import 'package:underworld_empire/screens/jobs_screen.dart';
import 'package:underworld_empire/screens/profile_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/storage/notification_preference_store.dart';

import 'fakes.dart';

final _t0 = DateTime.utc(2026, 10, 8, 12);

ScheduledNudge _nudge(String key, {Duration after = const Duration(hours: 1)}) => ScheduledNudge(
      kind: NudgeKind.energyFull,
      key: key,
      at: DateTime.now().toUtc().add(after),
      title: 'Title $key',
      body: 'Body $key',
    );

typedef _World = ({
  AppState state,
  FakeApiClient api,
  RecordingReturnNotifier notifier,
  InMemoryNotificationPreferenceStore store,
});

Future<_World> _signedIn({
  RecordingReturnNotifier? notifier,
  bool? saved,
  FakeApiClient? api,
}) async {
  final client = api ?? FakeApiClient();
  final recorder = notifier ?? RecordingReturnNotifier();
  final store = InMemoryNotificationPreferenceStore(saved);
  final state = AppState(
    apiClient: client,
    tokenStore: InMemoryTokenStore(),
    returnNotifier: recorder,
    notificationPreferenceStore: store,
    clock: () => _t0,
  );
  await state.bootstrap();
  await state.loadNotificationPreference();
  return (state: state, api: client, notifier: recorder, store: store);
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('nudgeId', () {
    test('is stable for a key, positive, and different for different keys', () {
      expect(nudgeId('energy'), nudgeId('energy'));
      expect(nudgeId('energy'), isNot(nudgeId('property:street-corner')));
      expect(nudgeId('property:a'), isNot(nudgeId('property:b')));
      for (final key in ['energy', 'property:street-corner', 'property:pawn-shop', '']) {
        expect(nudgeId(key), inInclusiveRange(0, 0x7fffffff));
      }
    });
  });

  group('SilentReturnNotifier', () {
    test('does nothing, asks for nothing and says it is unsupported', () async {
      const silent = SilentReturnNotifier();

      expect(silent.isSupported, isFalse);
      expect(await silent.requestPermission(), isFalse);
      await silent.replaceAll([_nudge('energy')]);
      await silent.cancelAll();
    });
  });

  group('LocalReturnNotifier', () {
    const channel = MethodChannel('dexterous.com/flutter/local_notifications');
    late List<MethodCall> calls;
    Object? Function(MethodCall call)? behaviour;

    setUp(() {
      // On a device the Android implementation registers itself at startup; a unit test has to.
      AndroidFlutterLocalNotificationsPlugin.registerWith();
      calls = [];
      behaviour = null;
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, (call) async {
        calls.add(call);
        final custom = behaviour?.call(call);
        if (custom != null) return custom;
        return call.method == 'initialize' || call.method == 'requestNotificationsPermission'
            ? true
            : null;
      });
    });

    tearDown(() {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, null);
    });

    test('clears what was scheduled, then schedules each nudge once, inexactly, in UTC', () async {
      final notifier = LocalReturnNotifier();

      final energy = _nudge('energy');
      await notifier.replaceAll([energy, _nudge('property:street-corner')]);

      final methods = calls.map((c) => c.method).toList();
      expect(methods, ['initialize', 'cancelAll', 'zonedSchedule', 'zonedSchedule']);

      final scheduled = calls.where((c) => c.method == 'zonedSchedule').toList();
      expect(scheduled.map((c) => c.arguments['id']),
          [nudgeId('energy'), nudgeId('property:street-corner')]);
      expect(scheduled.first.arguments['title'], 'Title energy');
      expect(scheduled.first.arguments['timeZoneName'], 'Etc/UTC');
      expect(DateTime.parse(scheduled.first.arguments['scheduledDateTimeISO8601']), energy.at);
      expect(scheduled.first.arguments['platformSpecifics']['scheduleMode'],
          'inexactAllowWhileIdle');
    });

    test('shows the white status-bar silhouette, not the coloured launcher icon', () async {
      await LocalReturnNotifier().replaceAll([_nudge('energy')]);

      final init = calls.firstWhere((c) => c.method == 'initialize');
      expect(init.arguments['defaultIcon'], 'ic_stat_nudge',
          reason: 'Android draws status-bar icons from alpha alone, so a coloured one is a blob');
    });

    test('reports whether the OS currently lets the app notify', () async {
      behaviour = (call) => call.method == 'areNotificationsEnabled' ? false : null;

      expect(await LocalReturnNotifier().isPermitted(), isFalse);
    });

    test('a permission query that fails is taken as still permitted', () async {
      behaviour = (call) {
        if (call.method == 'areNotificationsEnabled') throw PlatformException(code: 'boom');
        return null;
      };

      expect(await LocalReturnNotifier().isPermitted(), isTrue,
          reason: 'a plugin hiccup must not switch off a setting the player chose');
    });

    test('initialises the plugin once however many times it is used', () async {
      final notifier = LocalReturnNotifier();

      await notifier.replaceAll([_nudge('energy')]);
      await notifier.replaceAll([_nudge('energy')]);
      await notifier.cancelAll();

      expect(calls.where((c) => c.method == 'initialize'), hasLength(1));
    });

    test('an empty schedule just clears', () async {
      await LocalReturnNotifier().replaceAll(const []);

      expect(calls.map((c) => c.method), ['initialize', 'cancelAll']);
    });

    test('reports the permission the OS gave', () async {
      expect(await LocalReturnNotifier().requestPermission(), isTrue);

      behaviour = (call) => call.method == 'requestNotificationsPermission' ? false : null;
      expect(await LocalReturnNotifier().requestPermission(), isFalse);
    });

    test('a nudge the plugin rejects does not stop the others or throw', () async {
      behaviour = (call) {
        if (call.method == 'zonedSchedule' && call.arguments['id'] == nudgeId('energy')) {
          throw PlatformException(code: 'bad');
        }
        return null;
      };

      await LocalReturnNotifier().replaceAll([_nudge('energy'), _nudge('property:street-corner')]);

      expect(calls.where((c) => c.method == 'zonedSchedule'), hasLength(2));
    });

    test('a plugin that is missing or broken cannot crash the app', () async {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, null);
      final notifier = LocalReturnNotifier();

      expect(await notifier.requestPermission(), isFalse);
      await notifier.replaceAll([_nudge('energy')]);
      await notifier.cancelAll();
    });
  });

  group('AppState — the notification offer', () {
    Future<void> firstJob(_World s) async {
      await s.state.doJob('shake-down-corner-store');
      await pumpEventQueue();
    }

    test('offers after the first finished job but does not touch the OS', () async {
      final s = await _signedIn();
      expect(s.state.notificationOfferPending, isFalse, reason: 'nothing is offered at launch');

      await firstJob(s);

      expect(s.state.notificationOfferPending, isTrue);
      expect(s.notifier.permissionRequests, 0,
          reason: 'the OS prompt is spendable once, so only a yes may spend it');
      expect(await s.store.read(), isNull, reason: 'an open offer is not a decision');
    });

    test('yes spends the OS request once and remembers a grant', () async {
      final s = await _signedIn();
      await firstJob(s);

      await s.state.acceptNotificationOffer();

      expect(s.notifier.permissionRequests, 1);
      expect(s.state.notificationOfferPending, isFalse);
      expect(s.state.notificationsEnabled, isTrue);
      expect(await s.store.read(), isTrue);

      await firstJob(s);
      expect(s.notifier.permissionRequests, 1, reason: 'a later job never asks again');
      expect(s.state.notificationOfferPending, isFalse);
    });

    test('not now closes the offer and leaves the choice unasked', () async {
      final s = await _signedIn();
      await firstJob(s);

      s.state.declineNotificationOffer();

      expect(s.state.notificationOfferPending, isFalse);
      expect(s.notifier.permissionRequests, 0);
      expect(s.state.notificationsEnabled, isFalse);
      expect(await s.store.read(), isNull,
          reason: 'null is what keeps the one-shot OS prompt available later');
    });

    test('not now is not repeated on the next job of the same session', () async {
      final s = await _signedIn();
      await firstJob(s);
      s.state.declineNotificationOffer();

      await firstJob(s);

      expect(s.state.notificationOfferPending, isFalse);
    });

    test('an OS refusal after yes is remembered, says blocked, and is never re-offered', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(grantPermission: false));
      await firstJob(s);

      await s.state.acceptNotificationOffer();
      await firstJob(s);

      expect(s.notifier.permissionRequests, 1);
      expect(s.state.notificationsEnabled, isFalse);
      expect(s.state.notificationsBlocked, isTrue);
      expect(s.state.notificationOfferPending, isFalse);
      expect(await s.store.read(), isFalse);
    });

    test('does not offer a player who already chose', () async {
      final s = await _signedIn(saved: false);

      await firstJob(s);

      expect(s.state.notificationOfferPending, isFalse);
      expect(s.notifier.permissionRequests, 0);
    });

    test('does not offer on a device that cannot notify', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(supported: false));

      await firstJob(s);

      expect(s.state.notificationOfferPending, isFalse);
      expect(s.notifier.permissionRequests, 0);
      expect(s.state.notificationsAvailable, isFalse);
      expect(await s.store.read(), isNull, reason: 'no decision is recorded for what cannot work');
    });

    test('a job that is refused does not count as the first job', () async {
      final s = await _signedIn(api: FakeApiClient(refuseDoJobWith: 'energy'));

      await firstJob(s);

      expect(s.state.notificationOfferPending, isFalse);
    });

    test('turning the Rap Sheet switch on answers an open offer', () async {
      final s = await _signedIn();
      await firstJob(s);

      await s.state.setNotificationsEnabled(true);

      expect(s.state.notificationOfferPending, isFalse);
      expect(s.state.notificationsEnabled, isTrue);
    });

    test('an offer left open does not survive the app being torn down', () async {
      final s = await _signedIn();
      await firstJob(s);

      s.state.dispose();

      await expectLater(s.state.acceptNotificationOffer(), completes);
    });
  });

  group('AppState — the switch', () {
    test('turning on asks the OS and holds when it agrees', () async {
      final s = await _signedIn(saved: false);

      await s.state.setNotificationsEnabled(true);

      expect(s.state.notificationsEnabled, isTrue);
      expect(s.state.notificationsBlocked, isFalse);
      expect(await s.store.read(), isTrue);
    });

    test('turning on stays off and says blocked when the OS refuses', () async {
      final s = await _signedIn(
        notifier: RecordingReturnNotifier(grantPermission: false),
        saved: false,
      );

      await s.state.setNotificationsEnabled(true);

      expect(s.state.notificationsEnabled, isFalse);
      expect(s.state.notificationsBlocked, isTrue);
      expect(await s.store.read(), isFalse);
    });

    test('turning off clears what is scheduled and sticks', () async {
      final s = await _signedIn(saved: true);

      await s.state.setNotificationsEnabled(false);

      expect(s.state.notificationsEnabled, isFalse);
      expect(s.notifier.cancelCalls, 1);
      expect(await s.store.read(), isFalse);
    });
  });

  group('AppState — scheduling', () {
    test('schedules the energy nudge from the last snapshot when the player opted in', () async {
      final s = await _signedIn(saved: true);

      await s.state.scheduleReturnNudges();

      final energy = s.notifier.lastScheduled!.singleWhere((n) => n.key == 'energy');
      expect(energy.at, _t0.add(const Duration(seconds: 642)));
    });

    test('schedules the properties it has loaded', () async {
      final s = await _signedIn(saved: true);
      await s.state.loadProperties();

      await s.state.scheduleReturnNudges();

      final keys = s.notifier.lastScheduled!.map((n) => n.key);
      expect(keys, containsAll(['energy', 'property:street-corner', 'property:pawn-shop']));
    });

    test('schedules nothing for a player who opted out, and clears what was there', () async {
      final s = await _signedIn(saved: false);
      await s.state.loadProperties();

      await s.state.scheduleReturnNudges();

      expect(s.notifier.lastScheduled, isEmpty);
    });

    test('does nothing before there is a snapshot to schedule from', () async {
      final recorder = RecordingReturnNotifier();
      final state = AppState(
        apiClient: FakeApiClient(),
        tokenStore: InMemoryTokenStore(),
        returnNotifier: recorder,
      );

      await state.scheduleReturnNudges();

      expect(recorder.replacements, isEmpty);
    });

    test('does nothing on a device that cannot notify', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(supported: false), saved: true);

      await s.state.scheduleReturnNudges();
      await s.state.cancelReturnNudges();

      expect(s.notifier.replacements, isEmpty);
      expect(s.notifier.cancelCalls, 0);
    });

    test('a plugin that throws cannot take the app down', () async {
      final s = await _signedIn(
        notifier: RecordingReturnNotifier(failReplace: true, failCancel: true),
        saved: true,
      );

      await s.state.scheduleReturnNudges();
      await s.state.cancelReturnNudges();
    });
  });

  group('ReturnNotifierLifecycle', () {
    Future<void> pumpLifecycle(WidgetTester tester, AppState state) => tester.pumpWidget(
          MaterialApp(home: ReturnNotifierLifecycle(appState: state, child: const SizedBox())),
        );

    testWidgets('schedules when the app is backgrounded', (tester) async {
      final s = await _signedIn(saved: true);
      await pumpLifecycle(tester, s.state);

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await tester.pump();

      expect(s.notifier.replacements, hasLength(1));
      expect(s.notifier.cancelCalls, 0);
    });

    testWidgets('clears the nudges and re-reads the properties when the player is back',
        (tester) async {
      final s = await _signedIn(saved: true);
      await pumpLifecycle(tester, s.state);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await tester.pump();
      final propertyReadsBefore = s.api.propertiesCalls;

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pump();

      expect(s.notifier.cancelCalls, 1);
      expect(s.api.propertiesCalls, propertyReadsBefore + 1);
    });

    testWidgets('passing through inactive or hidden does nothing', (tester) async {
      final s = await _signedIn(saved: true);
      await pumpLifecycle(tester, s.state);

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
      await tester.pump();

      expect(s.notifier.replacements, isEmpty);
      expect(s.notifier.cancelCalls, 0);
    });

    testWidgets('stops listening once it leaves the tree', (tester) async {
      final s = await _signedIn(saved: true);
      await pumpLifecycle(tester, s.state);
      await tester.pumpWidget(const MaterialApp(home: SizedBox()));

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await tester.pump();

      expect(s.notifier.replacements, isEmpty);
    });
  });

  group('ProfileScreen — Return reminders', () {
    Future<Finder> pumpProfile(WidgetTester tester, AppState state) async {
      await tester.pumpWidget(MaterialApp(home: ProfileScreen(appState: state)));
      await tester.pumpAndSettle();
      final toggle = find.byKey(const Key('profile-notifications-toggle'));
      if (toggle.evaluate().isNotEmpty) {
        await tester.scrollUntilVisible(toggle, 200, scrollable: find.byType(Scrollable).first);
      }
      return toggle;
    }

    testWidgets('offers the switch, reflecting the saved choice', (tester) async {
      final s = await _signedIn(saved: true);

      final toggle = await pumpProfile(tester, s.state);

      expect(tester.widget<SwitchListTile>(toggle).value, isTrue);
    });

    testWidgets('is absent on a device that cannot notify', (tester) async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(supported: false));

      final toggle = await pumpProfile(tester, s.state);

      expect(toggle, findsNothing);
    });

    testWidgets('tapping it turns reminders on', (tester) async {
      final s = await _signedIn(saved: false);
      final toggle = await pumpProfile(tester, s.state);

      await tester.tap(toggle);
      await tester.pumpAndSettle();

      expect(s.state.notificationsEnabled, isTrue);
      expect(tester.widget<SwitchListTile>(toggle).value, isTrue);
    });

    testWidgets('says where to fix it when the OS refuses', (tester) async {
      final s = await _signedIn(
        notifier: RecordingReturnNotifier(grantPermission: false),
        saved: false,
      );
      final toggle = await pumpProfile(tester, s.state);

      await tester.tap(toggle);
      await tester.pumpAndSettle();

      expect(tester.widget<SwitchListTile>(toggle).value, isFalse);
      expect(find.byKey(const Key('profile-notifications-blocked')), findsOneWidget);
    });
  });

  group('JobsScreen — the notification offer', () {
    const offer = Key('notification-offer');
    const yes = Key('notification-offer-yes');
    const later = Key('notification-offer-later');
    const doJob = Key('do-job-shake-down-corner-store');

    Future<_World> pumpJobsAfterFirstJob(WidgetTester tester, {_World? world}) async {
      tester.view.physicalSize = const Size(800, 1400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final s = world ?? await _signedIn();
      await s.state.loadJobs();
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(
          body: JobsScreen(appState: s.state, refreshInterval: const Duration(hours: 1)),
        ),
      ));
      await tester.pumpAndSettle();
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));

      await s.state.doJob('shake-down-corner-store');
      await tester.pumpAndSettle();
      return s;
    }

    testWidgets('is not on screen until a job has finished', (tester) async {
      tester.view.physicalSize = const Size(800, 1400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final s = await _signedIn();
      await s.state.loadJobs();
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(
          body: JobsScreen(appState: s.state, refreshInterval: const Duration(hours: 1)),
        ),
      ));
      await tester.pumpAndSettle();
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));

      expect(find.byKey(offer), findsNothing);
    });

    testWidgets('appears after the first job with a plain question and two choices',
        (tester) async {
      await pumpJobsAfterFirstJob(tester);

      expect(find.byKey(offer), findsOneWidget);
      expect(find.text('Want a nudge when your energy is full?'), findsOneWidget);
      expect(find.byKey(yes), findsOneWidget);
      expect(find.byKey(later), findsOneWidget);
    });

    testWidgets('both choices are at least 48px tall', (tester) async {
      await pumpJobsAfterFirstJob(tester);

      expect(tester.getSize(find.byKey(yes)).height, greaterThanOrEqualTo(48));
      expect(tester.getSize(find.byKey(later)).height, greaterThanOrEqualTo(48));
    });

    testWidgets('yes asks the OS exactly once and the offer goes away', (tester) async {
      final s = await pumpJobsAfterFirstJob(tester);
      expect(s.notifier.permissionRequests, 0);

      await tester.tap(find.byKey(yes));
      await tester.pumpAndSettle();

      expect(s.notifier.permissionRequests, 1);
      expect(s.state.notificationsEnabled, isTrue);
      expect(find.byKey(offer), findsNothing);
    });

    testWidgets('not now never touches the OS and the offer goes away', (tester) async {
      final s = await pumpJobsAfterFirstJob(tester);

      await tester.tap(find.byKey(later));
      await tester.pumpAndSettle();

      expect(s.notifier.permissionRequests, 0);
      expect(await s.store.read(), isNull);
      expect(find.byKey(offer), findsNothing);
    });

    testWidgets('opening and closing it does not move the Do job button', (tester) async {
      await pumpJobsAfterFirstJob(tester);
      final open = tester.getTopLeft(find.byKey(doJob));

      await tester.tap(find.byKey(later));
      await tester.pumpAndSettle();

      expect(tester.getTopLeft(find.byKey(doJob)), open,
          reason: 'a bar that pushed the list down would make the next tap land on the wrong job');
    });

    testWidgets('is absent for a player who already chose', (tester) async {
      await pumpJobsAfterFirstJob(tester, world: await _signedIn(saved: false));

      expect(find.byKey(offer), findsNothing);
    });
  });

  group('AppState — a permission revoked in system settings', () {
    test('turns the setting off and says blocked, so the switch stops promising', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(permitted: false), saved: true);
      expect(s.state.notificationsEnabled, isTrue, reason: 'the saved choice says yes');

      await s.state.refreshNudgeSnapshot();

      expect(s.state.notificationsEnabled, isFalse);
      expect(s.state.notificationsBlocked, isTrue);
      expect(await s.store.read(), isFalse,
          reason: 'false, not null: this player has decided, and must not be offered again');
    });

    test('leaves the setting alone while the permission is still held', () async {
      final s = await _signedIn(saved: true);

      await s.state.refreshNudgeSnapshot();

      expect(s.state.notificationsEnabled, isTrue);
      expect(s.state.notificationsBlocked, isFalse);
    });

    test('does not second-guess a player who never opted in', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(permitted: false));

      await s.state.refreshNudgeSnapshot();

      expect(s.state.notificationsBlocked, isFalse);
      expect(await s.store.read(), isNull);
    });

    test('does not read the properties for a player it has just switched off', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(permitted: false), saved: true);
      final before = s.api.propertiesCalls;

      await s.state.refreshNudgeSnapshot();

      expect(s.api.propertiesCalls, before);
    });

    test('schedules nothing once switched off', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(permitted: false), saved: true);

      await s.state.refreshNudgeSnapshot();
      await s.state.scheduleReturnNudges();

      expect(s.notifier.lastScheduled, isEmpty);
    });

    test('is not checked on a device that cannot notify', () async {
      final s = await _signedIn(
        notifier: RecordingReturnNotifier(supported: false, permitted: false),
        saved: true,
      );

      await s.state.refreshNudgeSnapshot();

      expect(s.state.notificationsBlocked, isFalse);
      expect(await s.store.read(), isTrue);
    });
  });

  group('AppState — the nudge snapshot', () {
    test('an opted-in player gets one property read', () async {
      final s = await _signedIn(saved: true);
      final before = s.api.propertiesCalls;

      await s.state.refreshNudgeSnapshot();

      expect(s.api.propertiesCalls, before + 1);
    });

    test('an opted-out player is not read for', () async {
      final s = await _signedIn(saved: false);
      final before = s.api.propertiesCalls;

      await s.state.refreshNudgeSnapshot();

      expect(s.api.propertiesCalls, before,
          reason: 'the read banks nothing and nobody will be told, so it should not happen');
    });

    test('a player who has never been asked is not read for', () async {
      final s = await _signedIn();
      final before = s.api.propertiesCalls;

      await s.state.refreshNudgeSnapshot();

      expect(s.api.propertiesCalls, before);
    });

    test('a device that cannot notify is not read for', () async {
      final s = await _signedIn(
        notifier: RecordingReturnNotifier(supported: false),
        saved: true,
      );
      final before = s.api.propertiesCalls;

      await s.state.refreshNudgeSnapshot();

      expect(s.api.propertiesCalls, before, reason: 'web has no notifications to schedule');
    });

    test('granting the permission reads the properties, so the first background has them',
        () async {
      final s = await _signedIn();
      final before = s.api.propertiesCalls;

      await s.state.setNotificationsEnabled(true);

      expect(s.api.propertiesCalls, before + 1);
    });

    test('a refused permission reads nothing', () async {
      final s = await _signedIn(notifier: RecordingReturnNotifier(grantPermission: false));
      final before = s.api.propertiesCalls;

      await s.state.setNotificationsEnabled(true);

      expect(s.api.propertiesCalls, before);
    });

    testWidgets('an opted-out player makes no property read on resume', (tester) async {
      final s = await _signedIn(saved: false);
      await tester.pumpWidget(
        MaterialApp(home: ReturnNotifierLifecycle(appState: s.state, child: const SizedBox())),
      );
      final before = s.api.propertiesCalls;

      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pump();

      expect(s.api.propertiesCalls, before);
      expect(s.notifier.cancelCalls, 1, reason: 'cancelling on resume is unconditional');
    });
  });

  group('AppState — disposed mid-flight', () {
    test('disposing while the OS prompt is still open raises nothing', () async {
      final gate = Completer<void>();
      final notifier = RecordingReturnNotifier()..permissionGate = gate;
      final s = await _signedIn(notifier: notifier);
      final before = s.api.propertiesCalls;

      final pending = s.state.setNotificationsEnabled(true);
      await pumpEventQueue();
      s.state.dispose();
      gate.complete();

      await expectLater(pending, completes);
      expect(s.api.propertiesCalls, before,
          reason: 'a torn-down state has nothing to schedule, so it should not read either');
    });

    test('disposing while the saved choice is still loading raises nothing', () async {
      final gate = Completer<void>();
      final state = AppState(
        apiClient: FakeApiClient(),
        tokenStore: InMemoryTokenStore(),
        returnNotifier: RecordingReturnNotifier(),
        notificationPreferenceStore: _GatedPreferenceStore(gate),
        clock: () => _t0,
      );

      final pending = state.loadNotificationPreference();
      await pumpEventQueue();
      state.dispose();
      gate.complete();

      await expectLater(pending, completes);
    });
  });
}

/// Holds [read] open so a test can dispose [AppState] while the saved choice is still in flight.
class _GatedPreferenceStore implements NotificationPreferenceStore {
  _GatedPreferenceStore(this._gate);

  final Completer<void> _gate;

  @override
  Future<bool?> read() async {
    await _gate.future;
    return true;
  }

  @override
  Future<void> write(bool enabled) async {}
}
