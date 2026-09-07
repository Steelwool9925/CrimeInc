import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/widgets/app_shell.dart';

import 'fakes.dart';

/// Sets the test surface (and the view flutter_test actually measures against) to a controlled
/// logical width. flutter_test's default surface is 800x600 — right at the shell's breakpoint —
/// so every test here must move away from that boundary explicitly.
Future<void> setWidth(WidgetTester tester, double width) async {
  await tester.binding.setSurfaceSize(Size(width, 800));
  tester.view.physicalSize = Size(width, 800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

/// Pumps AppShell with a bootstrapped AppState.
Future<AppState> pumpShell(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: AppShell(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

/// Taps a bottom nav destination by its label.
///
/// Some destination labels (Jobs, Crew) also appear as titles on Home's own dashboard tiles, so a
/// bare `find.text(...)` is ambiguous whenever Home is on screen underneath the bar. Scoping the
/// finder to descend from the bar itself disambiguates without depending on tap order.
Future<void> tapNavLabel(WidgetTester tester, String label) async {
  await tester.tap(
    find.descendant(
      of: find.byType(BottomNavigationBar),
      matching: find.text(label),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('AppShell — breakpoint', () {
    testWidgets('narrow width shows the bottom bar, not the rail', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      expect(find.byKey(const Key('app-shell-narrow')), findsOneWidget);
      expect(find.byKey(const Key('app-shell-wide')), findsNothing);
      expect(find.byType(BottomNavigationBar), findsOneWidget);
      expect(find.byType(NavigationRail), findsNothing);
    });

    testWidgets('wide width shows the rail, not the bottom bar', (tester) async {
      await setWidth(tester, 1200);
      await pumpShell(tester, FakeApiClient());

      expect(find.byKey(const Key('app-shell-wide')), findsOneWidget);
      expect(find.byKey(const Key('app-shell-narrow')), findsNothing);
      expect(find.byType(NavigationRail), findsOneWidget);
      expect(find.byType(BottomNavigationBar), findsNothing);
    });
  });

  group('AppShell — navigation', () {
    testWidgets('tapping Jobs shows the Jobs screen content', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      await tapNavLabel(tester, 'Jobs');

      expect(find.byKey(const Key('job-card-shake-down-corner-store')), findsOneWidget);
    });

    testWidgets('tapping Crew shows the Crew coming-soon screen', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      await tapNavLabel(tester, 'Crew');

      expect(find.widgetWithText(AppBar, 'Crew'), findsOneWidget);
    });

    testWidgets('tapping Profile shows the Profile screen content', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      await tapNavLabel(tester, 'Profile');

      expect(find.byKey(const Key('profile-stat-cash')), findsOneWidget);
    });

    testWidgets('switching away from Home and back still shows Home content', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      // kFakeMe.cash is 12345 — shown on Home's cash card before any switch.
      expect(find.text('12345'), findsOneWidget);

      await tapNavLabel(tester, 'Jobs');
      expect(find.text('12345'), findsNothing);

      await tapNavLabel(tester, 'Home');

      expect(find.text('12345'), findsOneWidget);
    });
  });
}
