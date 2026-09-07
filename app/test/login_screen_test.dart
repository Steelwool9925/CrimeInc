import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/login_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

void main() {
  Future<void> enterCredentials(WidgetTester tester, {String username = 'alice'}) async {
    await tester.enterText(find.byKey(const Key('login-username-field')), username);
    await tester.enterText(find.byKey(const Key('login-password-field')), 'correct-horse-battery');
  }

  testWidgets('a successful login calls onLoggedIn and shows no error', (tester) async {
    final api = FakeApiClient(
      loginResult: const SessionResult(deviceToken: 'tok', playerId: 'id'),
    );
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    var loggedIn = false;

    await tester.pumpWidget(MaterialApp(
      home: LoginScreen(appState: state, onLoggedIn: () => loggedIn = true),
    ));

    await enterCredentials(tester);
    await tester.tap(find.byKey(const Key('login-submit')));
    // Not pumpAndSettle: same reasoning as ServerAddressScreen's tests — bounded pumps let the
    // awaited login() call run to completion without waiting out an indeterminate spinner.
    await tester.pump();
    await tester.pump();

    expect(loggedIn, isTrue);
    expect(api.loginCalls, [(username: 'alice', password: 'correct-horse-battery')]);
    expect(find.byKey(const Key('login-error')), findsNothing);
  });

  testWidgets('a refused login shows an error and does not call onLoggedIn', (tester) async {
    final api = FakeApiClient(refuseLoginWith: 'invalidCredentials');
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    var loggedIn = false;

    await tester.pumpWidget(MaterialApp(
      home: LoginScreen(appState: state, onLoggedIn: () => loggedIn = true),
    ));

    await enterCredentials(tester, username: 'nobody');
    await tester.tap(find.byKey(const Key('login-submit')));
    await tester.pump();
    await tester.pump();

    expect(loggedIn, isFalse);
    expect(find.byKey(const Key('login-error')), findsOneWidget);
  });
}
