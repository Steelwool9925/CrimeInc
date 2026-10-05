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

  testWidgets('an incomplete form is refused locally and never reaches the server',
      (tester) async {
    // The server used to answer an empty submit with "Incorrect username or password.", which is
    // untrue — nothing was typed — and spends one of the five attempts per five minutes the login
    // endpoint allows per IP, so a confused player could lock themselves out having never entered
    // a credential. Each missing-field case must be caught here and name the field that is missing.
    final api = FakeApiClient(
      loginResult: const SessionResult(deviceToken: 'tok', playerId: 'id'),
    );
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    var loggedIn = false;

    await tester.pumpWidget(MaterialApp(
      home: LoginScreen(appState: state, onLoggedIn: () => loggedIn = true),
    ));

    Future<String> submitAndReadError() async {
      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pump();
      return tester.widget<Text>(find.byKey(const Key('login-error'))).data!;
    }

    // Both blank.
    expect(await submitAndReadError(), 'Enter your username and password.');

    // Password only.
    await tester.enterText(find.byKey(const Key('login-password-field')), 'hunter2');
    expect(await submitAndReadError(), 'Enter your username.');

    // Username only. Whitespace does not count as a username.
    await tester.enterText(find.byKey(const Key('login-password-field')), '');
    await tester.enterText(find.byKey(const Key('login-username-field')), '   ');
    expect(await submitAndReadError(), 'Enter your username and password.');
    await tester.enterText(find.byKey(const Key('login-username-field')), 'alice');
    expect(await submitAndReadError(), 'Enter your password.');

    // Not one of those four submits was allowed to reach the server.
    expect(api.loginCalls, isEmpty);
    expect(loggedIn, isFalse);
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
