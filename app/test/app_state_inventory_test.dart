import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> bootstrapped(FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();
  return state;
}

void main() {
  group('AppState.loadInventory()', () {
    test('populates the catalog and the loadout', () async {
      final state = await bootstrapped(FakeApiClient());

      await state.loadInventory();

      expect(state.inventory, hasLength(3));
      expect(state.inventory.first.id, 'brass-knuckles');
      expect(state.loadout, isNotNull);
      expect(state.loadout!.totalAttack, 10); // base only — nothing equipped yet
      expect(state.inventoryError, isNull);
    });

    test('records an error when the catalog fails', () async {
      final state = await bootstrapped(FakeApiClient(throwOnInventory: true));

      await state.loadInventory();

      expect(state.inventoryError, isNotNull);
      expect(state.inventory, isEmpty);
    });

    test('does nothing before bootstrap (no token)', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.loadInventory();

      expect(api.inventoryCalls, 0);
    });
  });

  group('AppState.buyItem()', () {
    test('applies the player state the server returned', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadInventory();

      await state.buyItem('brass-knuckles');

      // The buy response carries the authoritative post-action player, so the cash figure the
      // player sees is the server's, not a local subtraction.
      expect(state.me!.cash, 25);
    });

    test('re-reads the catalog so gating cannot go stale', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadInventory();
      expect(api.inventoryCalls, 1);

      await state.buyItem('brass-knuckles');

      // Buying changes the cash every other row's canBuy was computed against. The server owns
      // that decision, so the client asks again rather than re-deriving it locally — which is how
      // the jobs list ended up with permanently dead buttons (Appendix A.1.2).
      expect(api.inventoryCalls, 2);
    });

    test('surfaces a refusal as a message rather than an error state', () async {
      final state = await bootstrapped(FakeApiClient(refuseItemActionWith: 'cash'));
      await state.loadInventory();

      await state.buyItem('leather-jacket');

      expect(state.actionMessage, contains('afford'));
      expect(state.inventoryError, isNull);
    });

    test('reports a level refusal in its own words', () async {
      final state = await bootstrapped(FakeApiClient(refuseItemActionWith: 'level'));
      await state.loadInventory();

      await state.buyItem('switchblade');

      expect(state.actionMessage, contains('level'));
    });

    test('ignores a second tap while one is in flight', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadInventory();

      // Both taps are issued before either completes. Without a guard the player is charged twice
      // for a single intent — the same defect Appendix A.1.3 records for skill allocation.
      await Future.wait([
        state.buyItem('brass-knuckles'),
        state.buyItem('brass-knuckles'),
      ]);

      expect(api.buyCalls, hasLength(1));
    });

    test('does nothing before bootstrap (no token)', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());

      await state.buyItem('brass-knuckles');

      expect(api.buyCalls, isEmpty);
    });
  });

  group('AppState.equipItem()', () {
    test('applies the loadout the server returned', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadInventory();

      await state.equipItem('brass-knuckles');

      // The figure CP3 watches: base 10 plus the weapon's 4.
      expect(state.loadout!.totalAttack, 14);
      expect(state.loadout!.itemAttack, 4);
    });

    test('re-reads the catalog so the equipped flags cannot go stale', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadInventory();

      await state.equipItem('brass-knuckles');

      expect(api.inventoryCalls, 2);
    });

    test('surfaces a refusal as a message', () async {
      final state = await bootstrapped(FakeApiClient(refuseItemActionWith: 'alreadyEquipped'));
      await state.loadInventory();

      await state.equipItem('brass-knuckles');

      expect(state.actionMessage, isNotNull);
      expect(state.inventoryError, isNull);
    });

    test('ignores a second tap while one is in flight', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadInventory();

      await Future.wait([
        state.equipItem('brass-knuckles'),
        state.equipItem('leather-jacket'),
      ]);

      expect(api.equipCalls, hasLength(1));
    });
  });

  group('AppState.unequipItem()', () {
    test('applies the loadout the server returned', () async {
      final api = FakeApiClient(
        unequipResult: EquipItemResult(
          equippedItemId: null,
          unequippedItemId: 'brass-knuckles',
          player: kFakeMe,
          loadout: kBareLoadout,
        ),
      );
      final state = await bootstrapped(api);
      await state.loadInventory();

      await state.unequipItem('brass-knuckles');

      expect(state.loadout!.itemAttack, 0);
      expect(state.loadout!.totalAttack, 10);
      expect(api.unequipCalls, contains('brass-knuckles'));
    });
  });

  group('itemInFlight', () {
    test('names the item being acted on and clears afterwards', () async {
      final state = await bootstrapped(FakeApiClient());
      await state.loadInventory();

      final pending = state.buyItem('brass-knuckles');
      expect(state.itemInFlight, 'brass-knuckles');

      await pending;
      expect(state.itemInFlight, isNull);
    });

    test('a second action cannot start while the post-action re-read is outstanding', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await state.loadInventory();

      // Hold the re-read open. Until it lands, the gating on screen still predates the buy, so a
      // second action started against it would be aimed at a picture the server has already moved
      // past — the staleness the re-read exists to close.
      final gate = Completer<void>();
      api.inventoryGate = gate;

      final first = state.buyItem('brass-knuckles');
      await pumpEventQueue();

      await state.equipItem('brass-knuckles');
      expect(api.equipCalls, isEmpty);
      expect(state.itemInFlight, 'brass-knuckles');

      gate.complete();
      await first;

      expect(state.itemInFlight, isNull);
    });
  });
}
