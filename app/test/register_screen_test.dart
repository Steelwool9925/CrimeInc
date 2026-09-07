import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/register_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

void main() {
  Future<AppState> bootstrapped(FakeApiClient api) async {
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    await state.bootstrap();
    return state;
  }

  Future<void> enterCredentials(WidgetTester tester, {String username = 'alicecarter'}) async {
    await tester.enterText(find.byKey(const Key('register-username-field')), username);
    await tester.enterText(
        find.byKey(const Key('register-password-field')), 'correct-horse-battery');
  }

  testWidgets('a successful registration pops back to the caller', (tester) async {
    final api = FakeApiClient();
    final state = await bootstrapped(api);

    // Push RegisterScreen the same way ProfileScreen does, so popping it is observable.
    await tester.pumpWidget(MaterialApp(
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(builder: (_) => RegisterScreen(appState: state)),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      ),
    ));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    await enterCredentials(tester);
    await tester.tap(find.byKey(const Key('register-submit')));
    // Not pumpAndSettle immediately after the tap — same reasoning as LoginScreen's tests: bounded
    // pumps let the awaited registerAccount() call, and the pop it triggers, run to completion.
    await tester.pump();
    await tester.pump();
    await tester.pumpAndSettle();

    expect(api.registerCalls, [
      (deviceToken: 'fake-token-abc', username: 'alicecarter', password: 'correct-horse-battery')
    ]);
    // Popped back to the caller screen.
    expect(find.byType(RegisterScreen), findsNothing);
    expect(find.text('open'), findsOneWidget);
  });

  testWidgets('each refusal token shows a distinct, non-token error message', (tester) async {
    Future<void> expectRefusal(String reason, {required String expectedSubstring}) async {
      final api = FakeApiClient(refuseRegisterWith: reason);
      final state = await bootstrapped(api);

      await tester.pumpWidget(MaterialApp(home: RegisterScreen(appState: state)));
      await enterCredentials(tester);
      await tester.tap(find.byKey(const Key('register-submit')));
      await tester.pump();
      await tester.pump();

      final errorWidget =
          tester.widget<Text>(find.byKey(const Key('register-error')));
      final message = errorWidget.data ?? '';

      expect(message, contains(expectedSubstring));
      expect(message, isNot(contains(reason))); // never the bare server token
    }

    await expectRefusal('alreadyClaimed', expectedSubstring: 'already has an account');
    await expectRefusal('usernameInvalid', expectedSubstring: 'username');
    await expectRefusal('passwordTooWeak', expectedSubstring: '8 characters');
    await expectRefusal('usernameTaken', expectedSubstring: 'already taken');
  });
}
