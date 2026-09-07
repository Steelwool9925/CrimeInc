import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

void main() {
  group('AppState.login()', () {
    test('a successful login finishes auth the same way bootstrap does', () async {
      final tokenStore = InMemoryTokenStore();
      final api = FakeApiClient(
        loginResult: const SessionResult(
          deviceToken: 'logged-in-token',
          playerId: 'player-guid-999',
        ),
      );
      final state = AppState(apiClient: api, tokenStore: tokenStore);

      final ok = await state.login('alice', 'correct-horse-battery');

      expect(ok, isTrue);
      expect(api.loginCalls, [(username: 'alice', password: 'correct-horse-battery')]);
      expect(state.status, LoadStatus.ready);
      expect(state.me, isNotNull);
      expect(state.loggingIn, isFalse);
      expect(state.loginError, isNull);
      // The returned token is persisted exactly like a guest bootstrap persists its own.
      expect(await tokenStore.read(), 'logged-in-token');
    });

    test('an invalid-credentials refusal sets a message and returns false', () async {
      final api = FakeApiClient(refuseLoginWith: 'invalidCredentials');
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      final ok = await state.login('alice', 'wrong-password');

      expect(ok, isFalse);
      expect(state.loginError, isNotNull);
      expect(state.loggingIn, isFalse);
      // Never surfaces the server's bare reason token as UI text.
      expect(state.loginError, isNot(contains('invalidCredentials')));
    });

    test('loginError is cleared at the start of a new attempt', () async {
      final api = FakeApiClient(refuseLoginWith: 'invalidCredentials');
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.login('alice', 'wrong-password');
      expect(state.loginError, isNotNull);

      final okApi = FakeApiClient(
        loginResult: const SessionResult(deviceToken: 'tok', playerId: 'id'),
      );
      final okState = AppState(apiClient: okApi, tokenStore: InMemoryTokenStore());
      await okState.login('alice', 'wrong-password');
      await okState.login('alice', 'correct-horse-battery');
      expect(okState.loginError, isNull);
    });

    test(
        'a failed me() after a successful login does not leave the token persisted '
        '(found by the final whole-branch code review)', () async {
      // login() itself succeeds against the server, but the follow-up me() call fails (a
      // transient network blip, say). login() must report failure to the UI — and, just as
      // importantly, must not have already written the token to storage: a player told "login
      // failed" who then relaunches the app must not be silently signed into that account anyway.
      final tokenStore = InMemoryTokenStore();
      final api = FakeApiClient(
        loginResult: const SessionResult(deviceToken: 'should-not-be-saved', playerId: 'id'),
        throwOnMe: true,
      );
      final state = AppState(apiClient: api, tokenStore: tokenStore);

      final ok = await state.login('alice', 'correct-horse-battery');

      expect(ok, isFalse);
      expect(state.loginError, isNotNull);
      expect(await tokenStore.read(), isNull);
    });

    test('loggingIn is true while the call is in flight', () async {
      final api = FakeApiClient(
        loginResult: const SessionResult(deviceToken: 'tok', playerId: 'id'),
      );
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      final future = state.login('alice', 'correct-horse-battery');
      // login() sets _loggingIn and calls notifyListeners synchronously before its first await,
      // so this is observable immediately after the call starts.
      expect(state.loggingIn, isTrue);

      await future;
      expect(state.loggingIn, isFalse);
    });
  });

  group('AppState.registerAccount()', () {
    Future<AppState> bootstrapped(FakeApiClient api) async {
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      return state;
    }

    test('a successful claim updates me from the server response', () async {
      const claimed = Me(
        playerId: 'player-guid-123',
        energy: MeterState(current: 7, max: 10, regenSecondsPerPoint: 300),
        stamina: MeterState(current: 5, max: 10, regenSecondsPerPoint: 300),
        health: MeterState(current: 10, max: 10, regenSecondsPerPoint: 300),
        cash: 12345,
        premiumCurrency: 0,
        level: 1,
        xp: 0,
        skillPoints: 0,
        attack: 1,
        defense: 1,
        username: 'alicecarter',
      );
      final api = FakeApiClient(registerResult: claimed);
      final state = await bootstrapped(api);

      final ok = await state.registerAccount('alicecarter', 'correct-horse-battery');

      expect(ok, isTrue);
      expect(state.me?.username, 'alicecarter');
      expect(api.registerCalls, [
        (
          deviceToken: 'fake-token-abc',
          username: 'alicecarter',
          password: 'correct-horse-battery'
        )
      ]);
      expect(state.registeringAccount, isFalse);
      expect(state.registerAccountError, isNull);
    });

    test('without a session yet, returns false and never calls the server', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      // Deliberately not bootstrapped — no device token exists yet.

      final ok = await state.registerAccount('alicecarter', 'correct-horse-battery');

      expect(ok, isFalse);
      expect(api.registerCalls, isEmpty);
    });

    test('each refusal token maps to its own message, never the bare token', () async {
      Future<String?> attempt(String reason) async {
        final api = FakeApiClient(refuseRegisterWith: reason);
        final state = await bootstrapped(api);
        final ok = await state.registerAccount('alicecarter', 'correct-horse-battery');
        expect(ok, isFalse);
        return state.registerAccountError;
      }

      final alreadyClaimed = await attempt('alreadyClaimed');
      final usernameInvalid = await attempt('usernameInvalid');
      final passwordTooWeak = await attempt('passwordTooWeak');
      final usernameTaken = await attempt('usernameTaken');

      expect(alreadyClaimed, isNot(usernameInvalid));
      expect(usernameInvalid, isNot(passwordTooWeak));
      expect(passwordTooWeak, isNot(usernameTaken));
      for (final message in [alreadyClaimed, usernameInvalid, passwordTooWeak, usernameTaken]) {
        expect(message, isNotNull);
        expect(message, isNot(contains('alreadyClaimed')));
        expect(message, isNot(contains('usernameInvalid')));
        expect(message, isNot(contains('passwordTooWeak')));
        expect(message, isNot(contains('usernameTaken')));
      }
    });
  });
}
