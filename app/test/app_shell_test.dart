import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/store_screen.dart';
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

  await tester.pumpWidget(
    MaterialApp(home: AppShell(appState: state, onChangeServer: () {})),
  );
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

    testWidgets('Inventory has a button of its own on the bottom bar', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      await tapNavLabel(tester, 'Inventory');

      expect(find.text('Brass Knuckles'), findsOneWidget);
      expect(find.byType(BottomNavigationBar), findsOneWidget, reason: 'a tab, not a pushed screen');
      expect(find.byTooltip('Back'), findsNothing);
    });

    testWidgets('Inventory has a button of its own on the wide rail', (tester) async {
      await setWidth(tester, 1200);
      await pumpShell(tester, FakeApiClient());

      await tester.tap(find.descendant(of: find.byType(NavigationRail), matching: find.byIcon(Icons.backpack_outlined)));
      await tester.pumpAndSettle();

      expect(find.text('Brass Knuckles'), findsOneWidget);
    });

    testWidgets('Crew is no longer a top-level destination; it stays a tile on Home', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());

      expect(
        find.descendant(of: find.byType(BottomNavigationBar), matching: find.text('Crew')),
        findsNothing,
      );
      expect(find.byKey(const Key('go-to-crew')), findsOneWidget);
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

  group('AppShell — Inventory as a tab', () {
    testWidgets("the empty state's Market button opens the Market over the shell and Back returns to the tab",
        (tester) async {
      await setWidth(tester, 400);
      await pumpShell(
        tester,
        FakeApiClient(
          inventoryResult: const InventoryList(
            items: [kUnaffordableArmor, kLockedWeapon],
            loadout: kBareLoadout,
            cash: 100,
            level: 1,
          ),
        ),
      );

      await tapNavLabel(tester, 'Inventory');
      await tester.tap(find.byKey(const Key('inv-empty-go-to-market')));
      await tester.pumpAndSettle();

      expect(find.byType(StoreScreen), findsOneWidget);

      await tester.pageBack();
      await tester.pumpAndSettle();

      expect(find.byType(StoreScreen), findsNothing);
      expect(find.byKey(const Key('inv-empty')), findsOneWidget, reason: 'the shell and its Inventory tab are still there');
      expect(find.byType(BottomNavigationBar), findsOneWidget);
    });
  });

  group('AppShell — resizing', () {
    Future<void> openMarketFromHome(WidgetTester tester) async {
      await tester.ensureVisible(find.byKey(const Key('go-to-store')));
      await tester.tap(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();
    }

    testWidgets('an open Market survives the window growing past the breakpoint and shrinking back', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());
      await openMarketFromHome(tester);
      expect(find.byType(StoreScreen), findsOneWidget);

      await setWidth(tester, 1200);
      await tester.pumpAndSettle();
      expect(find.byType(StoreScreen), findsOneWidget, reason: 'widening must not pop the Market');

      await setWidth(tester, 400);
      await tester.pumpAndSettle();
      expect(find.byType(StoreScreen), findsOneWidget, reason: 'narrowing must not pop it either');

      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('go-to-store')), findsOneWidget, reason: 'Back lands on Home');
    });

    testWidgets('the Market keeps its scroll position through a resize', (tester) async {
      await setWidth(tester, 400);
      await pumpShell(tester, FakeApiClient());
      await openMarketFromHome(tester);

      final scrollable = find.descendant(of: find.byType(StoreScreen), matching: find.byType(Scrollable)).first;
      final before = tester.state<ScrollableState>(scrollable);
      before.position.jumpTo(40);
      await tester.pumpAndSettle();
      final offset = before.position.pixels;

      await setWidth(tester, 1200);
      await tester.pumpAndSettle();

      final after = tester.state<ScrollableState>(
        find.descendant(of: find.byType(StoreScreen), matching: find.byType(Scrollable)).first,
      );
      expect(after.position.pixels, offset);
    });
  });

  group('AppShell — onChangeServer', () {
    testWidgets("forwards onChangeServer to Home's error screen", (tester) async {
      await setWidth(tester, 400);
      final api = FakeApiClient(throwOnSession: true);
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      var called = false;
      await tester.pumpWidget(MaterialApp(
        home: AppShell(appState: state, onChangeServer: () => called = true),
      ));
      await tester.pump();

      await tester.tap(find.widgetWithText(OutlinedButton, 'Change Server'));
      await tester.pump();

      expect(called, isTrue);
    });
  });
}
