import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/upgrade_advice.dart';

import 'fakes.dart';

/// The rule for telling a player about an upgrade. It is a pure function, so none of this needs a
/// widget pump — and the cases that matter are the ones where it should say nothing, because a
/// badge that over-fires turns into wallpaper.
void main() {
  Me withCash(int cash) => Me(
        playerId: kFakeMe.playerId,
        energy: kFakeMe.energy,
        stamina: kFakeMe.stamina,
        health: kFakeMe.health,
        cash: cash,
        premiumCurrency: 0,
        level: kFakeMe.level,
        xp: 0,
        skillPoints: 0,
        attack: kFakeMe.attack,
        defense: kFakeMe.defense,
      );

  InventoryItem item(
    String id, {
    String slot = 'weapon',
    int price = 100,
    int attack = 0,
    int defense = 0,
    int quantity = 0,
    bool equipped = false,
    bool canBuy = true,
    String? buyReason,
  }) =>
      InventoryItem(
        id: id,
        name: id,
        description: '',
        slot: slot,
        levelRequirement: 1,
        price: price,
        attack: attack,
        defense: defense,
        quantity: quantity,
        equipped: equipped,
        canBuy: canBuy,
        buyUnavailableReason: buyReason,
        canEquip: quantity > 0,
      );

  group('adviseUpgrade', () {
    test('says nothing when there is nothing to buy', () {
      expect(adviseUpgrade(withCash(1000), const []), isNull);
    });

    test('says nothing when nothing is affordable', () {
      final items = [item('blade', price: 500, attack: 7, canBuy: false, buyReason: 'cash')];

      expect(adviseUpgrade(withCash(100), items), isNull);
    });

    test('says nothing for an item the server locked behind a level, however much cash there is', () {
      final items = [item('blade', price: 100, attack: 7, canBuy: false, buyReason: 'level')];

      expect(adviseUpgrade(withCash(100000), items), isNull);
    });

    test('says nothing when the item is worse than what is already worn', () {
      final items = [
        item('rifle', price: 2000, attack: 34, quantity: 1, equipped: true),
        item('knuckles', price: 75, attack: 4),
      ];

      expect(adviseUpgrade(withCash(5000), items), isNull);
    });

    test('says nothing for an item that is no better than what is worn', () {
      final items = [
        item('blade', price: 180, attack: 7, quantity: 1, equipped: true),
        item('blade-two', price: 200, attack: 7),
      ];

      expect(adviseUpgrade(withCash(5000), items), isNull);
    });

    test('names an affordable item that beats what is worn, and how much better it is', () {
      final items = [
        item('knuckles', price: 75, attack: 4, quantity: 1, equipped: true),
        item('blade', price: 180, attack: 7, defense: 1),
      ];

      final advice = adviseUpgrade(withCash(500), items)!;

      expect(advice.item.id, 'blade');
      expect(advice.attackGain, 3);
      expect(advice.defenseGain, 1);
      expect(advice.gainLabel, '+3 attack, +1 defense');
    });

    test('names the worn item it would replace, and nothing for an empty slot', () {
      final replacing = adviseUpgrade(withCash(500), [
        item('knuckles', price: 75, attack: 4, quantity: 1, equipped: true),
        item('blade', price: 180, attack: 7, defense: 1),
      ])!;
      expect(replacing.replaces?.id, 'knuckles');
      expect(replacing.comparisonLabel, '+3 attack, +1 defense more than your knuckles');

      final filling = adviseUpgrade(withCash(500), [item('jacket', slot: 'armor', price: 120, defense: 5)])!;
      expect(filling.replaces, isNull);
      expect(filling.comparisonLabel, isNull);
    });

    test('an empty slot counts the whole of the item as the gain', () {
      final items = [item('jacket', slot: 'armor', price: 120, defense: 5)];

      final advice = adviseUpgrade(withCash(500), items)!;

      expect(advice.defenseGain, 5);
      expect(advice.gainLabel, '+5 defense');
    });

    test('compares within the slot only: a weapon is not measured against worn armor', () {
      final items = [
        item('jacket', slot: 'armor', price: 120, defense: 5, quantity: 1, equipped: true),
        item('knuckles', price: 75, attack: 4),
      ];

      expect(adviseUpgrade(withCash(500), items)!.item.id, 'knuckles');
    });

    test('an item the server refused for cash counts once the player now has the cash', () {
      // The catalog was read when the player was short; jobs have paid since.
      final items = [item('blade', price: 180, attack: 7, canBuy: false, buyReason: 'cash')];

      expect(adviseUpgrade(withCash(100), items), isNull);
      expect(adviseUpgrade(withCash(180), items)!.item.id, 'blade');
    });

    test('never offers an item the player already owns', () {
      final items = [item('blade', price: 180, attack: 7, quantity: 1)];

      expect(adviseUpgrade(withCash(500), items), isNull);
    });

    test('never offers what the player cannot cover even when the catalog says it could be bought', () {
      // A stale catalog and a fresher cash figure: the cash on hand is the later fact.
      final items = [item('blade', price: 180, attack: 7)];

      expect(adviseUpgrade(withCash(50), items), isNull);
    });

    test('the cheapest qualifying item wins', () {
      final items = [
        item('shotgun', price: 600, attack: 15),
        item('blade', price: 180, attack: 7),
        item('knuckles', price: 75, attack: 4),
      ];

      expect(adviseUpgrade(withCash(1000), items)!.item.id, 'knuckles');
    });

    test('at equal prices the bigger improvement wins, then the id', () {
      final bigger = [
        item('b', price: 100, attack: 5),
        item('a', price: 100, attack: 9),
      ];
      expect(adviseUpgrade(withCash(500), bigger)!.item.id, 'a');

      final tied = [
        item('zed', price: 100, attack: 5),
        item('alpha', price: 100, attack: 5),
      ];
      expect(adviseUpgrade(withCash(500), tied)!.item.id, 'alpha');
    });

    test('a stat the item lowers is not reported as a gain, but a better total still qualifies', () {
      final items = [
        item('knuckles', price: 75, attack: 4, defense: 6, quantity: 1, equipped: true),
        item('blade', price: 180, attack: 10, defense: 1),
      ];

      final advice = adviseUpgrade(withCash(500), items)!;

      expect(advice.attackGain, 6);
      expect(advice.defenseGain, 0);
      expect(advice.gainLabel, '+6 attack');
    });
  });
}
