import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/login_screen.dart';
import 'package:underworld_empire/screens/welcome_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

void main() {
  AppState freshState() =>
      AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());

  Future<void> pump(
    WidgetTester tester, {
    required VoidCallback onContinueAsGuest,
    required VoidCallback onLoggedIn,
  }) =>
      tester.pumpWidget(MaterialApp(
        home: WelcomeScreen(
          appState: freshState(),
          onContinueAsGuest: onContinueAsGuest,
          onLoggedIn: onLoggedIn,
        ),
      ));

  testWidgets('offers both a guest continuation and a login route', (tester) async {
    await pump(tester, onContinueAsGuest: () {}, onLoggedIn: () {});

    expect(find.byKey(const Key('welcome-continue-as-guest')), findsOneWidget);
    expect(find.byKey(const Key('welcome-log-in')), findsOneWidget);
  });

  testWidgets('tapping "continue as new player" calls onContinueAsGuest', (tester) async {
    var called = false;
    await pump(tester, onContinueAsGuest: () => called = true, onLoggedIn: () {});

    await tester.tap(find.byKey(const Key('welcome-continue-as-guest')));
    await tester.pump();

    expect(called, isTrue);
  });

  testWidgets('tapping "log in" navigates to LoginScreen', (tester) async {
    await pump(tester, onContinueAsGuest: () {}, onLoggedIn: () {});

    await tester.tap(find.byKey(const Key('welcome-log-in')));
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsOneWidget);
    expect(find.byKey(const Key('login-username-field')), findsOneWidget);
  });
}
