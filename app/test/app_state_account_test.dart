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
}
