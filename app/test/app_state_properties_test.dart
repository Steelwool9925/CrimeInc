import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> bootstrapped(FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  return state;
}

void main() {
  group('AppState.loadProperties()', () {
    test('populates the whole catalog, owned or not', () async {
      final state = await bootstrapped(FakeApiClient());

      await state.loadProperties();

      // The listing carries every property in the pack, not only the held ones, so the buy half
      // and the collect half of the screen are two views of one list.
      expect(state.properties, hasLength(3));
      expect(state.properties.first.id, 'street-corner');
      expect(state.propertiesError, isNull);
    });

    test('records an error when the catalog fails', () async {
      final state = await bootstrapped(FakeApiClient(throwOnProperties: true));

      await state.loadProperties();

      expect(state.propertiesError, isNotNull);
      expect(state.properties, isEmpty);
    });

    test('does nothing before bootstrap (no token)', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.loadProperties();

      expect(api.propertiesCalls, 0);
    });

    test('collapses concurrent loads into one request', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);

      await Future.wait([state.loadProperties(), state.loadProperties()]);

      expect(api.propertiesCalls, 1);
    });
  });

  group('AppState.ownedProperties', () {
    test('is the held rows, which is what the holdings half lists', () async {
      final state = await bootstrapped(FakeApiClient());

      await state.loadProperties();

      expect(
        state.ownedProperties.map((p) => p.id),
        ['street-corner', 'pawn-shop'],
      );
    });
  });

  group('AppState.buyProperty()', () {
    test('applies the player state the server returned', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadProperties();

      await state.buyProperty('street-corner');

      // 600 less the corner's 500, from the server's post-action player rather than a local
      // subtraction.
      expect(state.me!.cash, 100);
    });

    test('re-reads the catalog so gating cannot go stale', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadProperties();
      expect(api.propertiesCalls, 1);

      await state.buyProperty('street-corner');

      // Buying moves the cash every other row's canBuy was computed against, and the quantity its
      // own cap is scaled by. The server owns both decisions, so the client asks again rather than
      // re-deriving them locally (Appendix A.1.2).
      expect(api.propertiesCalls, 2);
    });

    test('the re-read catalog carries the new holding', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadProperties();

      await state.buyProperty('street-corner');

      final corner =
          state.properties.firstWhere((p) => p.id == 'street-corner');
      expect(corner.quantity, 2);
      expect(corner.cap, 120);
    });

    test('surfaces a refusal as a message rather than an error state', () async {
      final state =
          await bootstrapped(FakeApiClient(refusePropertyActionWith: 'cash'));
      await state.loadProperties();

      await state.buyProperty('street-corner');

      expect(state.actionMessage, contains('afford'));
      expect(state.propertiesError, isNull);
    });

    test('names the level as the obstacle when that is the refusal', () async {
      final state =
          await bootstrapped(FakeApiClient(refusePropertyActionWith: 'level'));
      await state.loadProperties();

      await state.buyProperty('car-wash');

      expect(state.actionMessage, contains('level'));
    });
  });

  group('AppState.collectProperty()', () {
    test('applies the player state the server returned', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadProperties();

      await state.collectProperty('street-corner');

      // 600 plus the corner's 30 takings — the figure CP3 watches rise.
      expect(state.me!.cash, 630);
    });

    test('sends the collection for the property asked for', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadProperties();

      await state.collectProperty('street-corner');

      expect(api.collectPropertyCalls, ['street-corner']);
    });

    test('re-reads the catalog so the emptied counter is the server\'s', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadProperties();

      await state.collectProperty('street-corner');

      expect(api.propertiesCalls, 2);
      final corner =
          state.properties.firstWhere((p) => p.id == 'street-corner');
      expect(corner.pending, 0);
      expect(corner.canCollect, isFalse);
    });

    test('surfaces an empty counter as a message, not an error', () async {
      final state = await bootstrapped(
          FakeApiClient(refusePropertyActionWith: 'nothingToCollect'));
      await state.loadProperties();

      await state.collectProperty('pawn-shop');

      expect(state.actionMessage, contains('earned'));
      expect(state.propertiesError, isNull);
    });

    test('surfaces a property the player does not hold as a message', () async {
      final state = await bootstrapped(
          FakeApiClient(refusePropertyActionWith: 'notOwned'));
      await state.loadProperties();

      await state.collectProperty('car-wash');

      expect(state.actionMessage, contains('own'));
    });
  });

  group('propertyInFlight', () {
    test('names the property being acted on and clears afterwards', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadProperties();

      final pending = state.buyProperty('street-corner');
      expect(state.propertyInFlight, 'street-corner');

      await pending;
      expect(state.propertyInFlight, isNull);
    });

    test('ignores a second tap while one is in flight', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadProperties();

      await Future.wait([
        state.collectProperty('street-corner'),
        state.collectProperty('pawn-shop'),
      ]);

      expect(api.collectPropertyCalls, hasLength(1));
    });

    test('a second action cannot start while the post-action re-read is outstanding',
        () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadProperties();

      // Hold the re-read open. Until it lands the rows on screen still carry the gating the server
      // computed before the collect, so a second action aimed at them would be aimed at a picture
      // the server has already moved past — the staleness the re-read exists to close, and the bug
      // found in S3.3.
      final gate = Completer<void>();
      api.propertiesGate = gate;

      final first = state.collectProperty('street-corner');
      await pumpEventQueue();

      await state.buyProperty('street-corner');
      expect(api.buyPropertyCalls, isEmpty);
      expect(state.propertyInFlight, 'street-corner');

      gate.complete();
      await first;

      expect(state.propertyInFlight, isNull);
    });
  });
}
