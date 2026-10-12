import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/storage/upgrade_seen_store.dart';

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

  group('AppState — catalog reads racing an item action', () {
    test('a read already in flight cannot leave the catalog on its pre-purchase picture', () async {
      // The bootstrap read is held open, so it will answer with the catalog as it was before the buy.
      final gate = Completer<void>();
      const unowned = InventoryItem(
        id: 'brass-knuckles',
        name: 'Brass Knuckles',
        description: 'Cheap, quiet and entirely deniable.',
        slot: 'weapon',
        levelRequirement: 1,
        price: 75,
        attack: 4,
        defense: 0,
        quantity: 0,
        equipped: false,
        canBuy: true,
        canEquip: false,
      );
      final api = FakeApiClient(
        inventoryResult: const InventoryList(items: [unowned], loadout: kBareLoadout, cash: 100, level: 1),
      )..inventoryGate = gate;
      final state = await bootstrapped(api);
      await pumpEventQueue();
      expect(state.inventory, isEmpty, reason: 'the bootstrap read is still held');

      final buy = state.buyItem('brass-knuckles');
      await pumpEventQueue();
      gate.complete();
      await buy;
      await pumpEventQueue();

      expect(api.buyCalls, ['brass-knuckles']);
      final knuckles = state.inventory.firstWhere((i) => i.id == 'brass-knuckles');
      expect(knuckles.quantity, greaterThan(0), reason: 'the stale bootstrap read must not win');
      expect(state.inventoryLoading, isFalse);
      expect(state.itemInFlight, isNull);
    });
  });

  group('AppState — upgrade advice', () {
    Me meWith({int? cash, int? level}) => Me(
          playerId: kFakeMe.playerId,
          energy: kFakeMe.energy,
          stamina: kFakeMe.stamina,
          health: kFakeMe.health,
          cash: cash ?? kFakeMe.cash,
          premiumCurrency: 0,
          level: level ?? kFakeMe.level,
          xp: 0,
          skillPoints: 0,
          attack: kFakeMe.attack,
          defense: kFakeMe.defense,
        );

    Future<void> settle() => Future<void>.delayed(Duration.zero);

    test('bootstrap reads the catalog so the advice exists on the first Home', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();

      expect(api.inventoryCalls, 1);
      expect(state.upgradeAdvice!.item.id, 'leather-jacket');
    });

    test('bootstrap does not wait for the catalog: the player is ready while it is still loading', () async {
      final api = FakeApiClient()..inventoryGate = Completer<void>();
      final state = await bootstrapped(api);

      expect(state.status, LoadStatus.ready, reason: 'Home must not wait on the catalog');
      expect(state.upgradeAdvice, isNull);

      api.inventoryGate!.complete();
      await settle();

      expect(state.upgradeAdvice, isNotNull);
    });

    test('a failed bootstrap read costs only the advice', () async {
      final api = FakeApiClient(throwOnInventory: true);
      final state = await bootstrapped(api);
      await settle();

      expect(state.status, LoadStatus.ready);
      expect(state.upgradeAdvice, isNull);
    });

    test('a change in cash moves the advice without any request', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();
      expect(state.upgradeAdvice, isNotNull);

      api.meOverride = meWith(cash: 50);
      await state.refresh();

      expect(state.upgradeAdvice, isNull, reason: 'the jacket costs 120');
      expect(api.inventoryCalls, 1, reason: 'cash alone never re-reads the catalog');
    });

    test('a level-up re-reads the catalog once, and an unchanged level does not', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();
      expect(api.inventoryCalls, 1);

      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 1, reason: 'same level, nothing to re-read');

      api.meOverride = meWith(level: 2);
      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 2, reason: 'the level gate on every row may have moved');

      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 2, reason: 'one read per level-up, not per poll');
    });

    test('a level-up while a read is still in flight is not swallowed by it, and polls do not stack reads', () async {
      final gate = Completer<void>();
      final api = FakeApiClient()..inventoryGate = gate;
      final state = await bootstrapped(api);
      await settle();
      expect(api.inventoryCalls, 1, reason: 'the bootstrap read, held open at level 1');

      api.meOverride = meWith(level: 2);
      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 2, reason: 'the level-up needs its own read; the one in flight predates it');

      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 2, reason: 'a read for this level is already on its way');

      gate.complete();
      await pumpEventQueue();
      await state.refresh();
      await settle();
      expect(api.inventoryCalls, 2, reason: 'the newer read recorded level 2, so none is owed');
      expect(state.inventoryLoading, isFalse);
    });

    test('asking for the advice again does not recompute it until cash or the catalog changes', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();

      expect(identical(state.upgradeAdvice, state.upgradeAdvice), isTrue);
    });
  });

  group('AppState — the upgrade badge across a reload', () {
    Future<void> settle() => Future<void>.delayed(Duration.zero);

    Future<AppState> start(FakeApiClient api, UpgradeSeenStore store) async {
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore(), seenStore: store);
      await state.bootstrap();
      await settle();
      return state;
    }

    test('stays cleared when the same player comes back to the same advised item', () async {
      final store = InMemoryUpgradeSeenStore();
      final first = await start(FakeApiClient(), store);
      first.markUpgradeSeen();
      await settle();

      final afterReload = await start(FakeApiClient(), store);

      expect(afterReload.upgradeAdvice!.item.id, first.upgradeAdvice!.item.id);
      expect(afterReload.upgradeBadgeVisible, isFalse);
    });

    test('comes back for an item the player has not been shown', () async {
      final store = InMemoryUpgradeSeenStore();
      await store.write(kFakeMe.playerId, 'some-other-item');

      final state = await start(FakeApiClient(), store);

      expect(state.upgradeBadgeVisible, isTrue);
    });

    test('one player having seen an item says nothing about another', () async {
      final store = InMemoryUpgradeSeenStore();
      final first = await start(FakeApiClient(), store);
      first.markUpgradeSeen();
      await settle();

      final other = await start(FakeApiClient(meResult: _otherPlayer()), store);

      expect(other.upgradeBadgeVisible, isTrue);
    });

    test('a store that never answers cannot hold Home on the loading screen', () async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore(), seenStore: _HangingSeenStore());

      await state.bootstrap().timeout(const Duration(seconds: 2));
      await settle();

      expect(state.status, LoadStatus.ready);
      expect(state.upgradeAdvice, isNotNull, reason: 'the words do not depend on the memory');
      expect(state.upgradeBadgeVisible, isFalse, reason: 'the dot waits for the memory rather than guess');
    });

    test('opening the Market before the memory arrives is not undone by it arriving', () async {
      final late = _LateSeenStore('leather-jacket');
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore(), seenStore: late);
      await state.bootstrap();
      await settle();

      state.markUpgradeSeen();
      late.release();
      await settle();

      expect(state.upgradeBadgeVisible, isFalse);
    });

    test('a store that cannot be read costs only the memory, never the dot or the app', () async {
      final state = await start(FakeApiClient(), _BrokenSeenStore());

      expect(state.status, LoadStatus.ready);
      expect(state.upgradeBadgeVisible, isTrue);

      state.markUpgradeSeen();
      await settle();

      expect(state.upgradeBadgeVisible, isFalse, reason: 'still cleared for this session');
    });
  });

  group('AppState — the upgrade badge', () {
    Future<void> settle() => Future<void>.delayed(Duration.zero);

    test('shows while there is advice the player has not been shown', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();

      expect(state.upgradeAdvice, isNotNull);
      expect(state.upgradeBadgeVisible, isTrue);
    });

    test('clears once the Market has been opened, but the advice itself stays', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();

      state.markUpgradeSeen();

      expect(state.upgradeBadgeVisible, isFalse);
      expect(state.upgradeAdvice, isNotNull, reason: 'the words stay; only the attention dot clears');
    });

    test('marking it seen again notifies nobody', () async {
      final state = await bootstrapped(FakeApiClient());
      await settle();
      state.markUpgradeSeen();

      var notified = 0;
      state.addListener(() => notified++);
      state.markUpgradeSeen();
      state.markUpgradeSeen();

      expect(notified, 0);
    });

    test('with nothing advised there is nothing to see', () async {
      final state = await bootstrapped(FakeApiClient(throwOnInventory: true));
      await settle();

      state.markUpgradeSeen();

      expect(state.upgradeBadgeVisible, isFalse);
    });

    test('a different item being advised later raises it again', () async {
      final api = FakeApiClient();
      final state = await bootstrapped(api);
      await settle();
      state.markUpgradeSeen();
      expect(state.upgradeBadgeVisible, isFalse);

      // The jacket (120) was the first advice. Buying it makes the switchblade tier the next one.
      await state.buyItem('leather-jacket');
      await settle();
      final next = state.upgradeAdvice;

      if (next != null) {
        expect(state.upgradeBadgeVisible, isTrue, reason: 'a new recommendation is news');
      } else {
        expect(state.upgradeBadgeVisible, isFalse);
      }
    });
  });
}

Me _otherPlayer() => Me(
      playerId: 'someone-else',
      energy: kFakeMe.energy,
      stamina: kFakeMe.stamina,
      health: kFakeMe.health,
      cash: kFakeMe.cash,
      premiumCurrency: 0,
      level: kFakeMe.level,
      xp: 0,
      skillPoints: 0,
      attack: kFakeMe.attack,
      defense: kFakeMe.defense,
    );

class _BrokenSeenStore implements UpgradeSeenStore {
  @override
  Future<String?> read(String playerId) async => throw StateError('storage unavailable');

  @override
  Future<void> write(String playerId, String itemId) async => throw StateError('storage unavailable');
}

class _HangingSeenStore implements UpgradeSeenStore {
  @override
  Future<String?> read(String playerId) => Completer<String?>().future;

  @override
  Future<void> write(String playerId, String itemId) async {}
}

class _LateSeenStore implements UpgradeSeenStore {
  _LateSeenStore(this._answer);

  final String _answer;
  final _gate = Completer<void>();

  void release() => _gate.complete();

  @override
  Future<String?> read(String playerId) async {
    await _gate.future;
    return _answer;
  }

  @override
  Future<void> write(String playerId, String itemId) async {}
}
