import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

void main() {
  group('AppState.bootstrap()', () {
    test('with no stored token calls session(null) and persists returned token',
        () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();

      expect(api.sessionCalls, hasLength(1));
      expect(api.sessionCalls.first, isNull);
      expect(state.status, LoadStatus.ready);
      expect(state.me, isNotNull);
      expect(await store.read(), 'fake-token-abc');
    });

    test('second bootstrap sends the stored token', () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();
      await state.bootstrap();

      expect(api.sessionCalls, hasLength(2));
      expect(api.sessionCalls[0], isNull);
      expect(api.sessionCalls[1], 'fake-token-abc');
    });

    test('sets status to ready after successful bootstrap', () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();

      expect(state.status, LoadStatus.ready);
      expect(state.me!.cash, 12345);
      expect(state.contentPack!.cashLabel, 'Cash');
    });

    test('sets status to error when session fails', () async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();

      expect(state.status, LoadStatus.error);
      expect(state.errorMessage, isNotNull);
    });

    test('continues bootstrap even if contentPack fails (best-effort)',
        () async {
      final api = FakeApiClient(throwOnContentPack: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();

      expect(state.status, LoadStatus.ready);
      expect(state.contentPack, isNull);
      expect(state.me, isNotNull);
    });

    test('sets status error when /me fails', () async {
      final api = FakeApiClient(throwOnMe: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();

      expect(state.status, LoadStatus.error);
    });
  });

  group('AppState.refresh()', () {
    test('refresh fetches updated me data', () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();
      final countBefore = api.meCalls;
      await state.refresh();

      expect(api.meCalls, countBefore + 1);
    });

    test('refresh does nothing before bootstrap (no token)', () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      // No bootstrap — _deviceToken is null
      await state.refresh();

      expect(api.meCalls, 0);
    });
  });

  group('AppState polling', () {
    test('stopPolling cancels timer without error', () async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);

      await state.bootstrap();
      state.startPolling(const Duration(minutes: 5));
      state.stopPolling();
      state.dispose();
    });
  });
}
