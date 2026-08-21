import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';

/// One catalog row as GET /properties sends it.
Map<String, dynamic> propertyJson({
  int quantity = 1,
  int pending = 30,
  int cap = 60,
  bool canBuy = true,
  String? buyReason,
  bool canCollect = true,
  String? collectReason,
}) =>
    {
      'id': 'street-corner',
      'name': 'Street Corner',
      'description': 'A patch of pavement nobody works without your say-so.',
      'levelRequirement': 1,
      'price': 500,
      'incomePerHour': 240,
      'accrualCapMinutes': 15,
      'quantity': quantity,
      'pending': pending,
      'cap': cap,
      'canBuy': canBuy,
      'buyUnavailableReason': buyReason,
      'canCollect': canCollect,
      'collectUnavailableReason': collectReason,
    };

/// A minimal /me body, enough for the nested player on an action response.
Map<String, dynamic> meJson({required int cash}) => {
      'playerId': 'player-guid-123',
      'energy': meterJson(),
      'stamina': meterJson(),
      'health': meterJson(),
      'cash': cash,
      'premiumCurrency': 0,
      'level': 2,
      'xp': 0,
      'skillPoints': 0,
      'attack': 10,
      'defense': 10,
    };

Map<String, dynamic> meterJson() => {
      'current': 7,
      'max': 10,
      'regenSecondsPerPoint': 300,
      'secondsUntilNextPoint': 42,
    };

void main() {
  group('Property.fromJson', () {
    test('reads every field the listing sends', () {
      final property = Property.fromJson(propertyJson());

      expect(property.id, 'street-corner');
      expect(property.name, 'Street Corner');
      expect(property.description, startsWith('A patch of pavement'));
      expect(property.levelRequirement, 1);
      expect(property.price, 500);
      expect(property.incomePerHour, 240);
      expect(property.accrualCapMinutes, 15);
      expect(property.quantity, 1);
      expect(property.pending, 30);
      expect(property.cap, 60);
      expect(property.canBuy, isTrue);
      expect(property.canCollect, isTrue);
    });

    test('keeps the server reason tokens verbatim', () {
      final property = Property.fromJson(propertyJson(
        canBuy: false,
        buyReason: 'level',
        canCollect: false,
        collectReason: 'notOwned',
      ));

      expect(property.buyUnavailableReason, 'level');
      expect(property.collectUnavailableReason, 'notOwned');
    });

    test('reads an absent reason as no reason', () {
      final property = Property.fromJson(propertyJson());

      expect(property.buyUnavailableReason, isNull);
      expect(property.collectUnavailableReason, isNull);
    });
  });

  group('Property.isOwned', () {
    test('is true when the player holds at least one unit', () {
      expect(Property.fromJson(propertyJson(quantity: 1)).isOwned, isTrue);
    });

    test('is false when the player holds none', () {
      expect(Property.fromJson(propertyJson(quantity: 0)).isOwned, isFalse);
    });
  });

  group('Property.projectedPending', () {
    // Display-only interpolation between polls. The rate is per unit per hour, so 240/hour is four
    // a minute for a single unit. It never decides whether collecting is allowed — that stays the
    // server's `canCollect`.
    test('is the served figure at the instant it was served', () {
      final property = Property.fromJson(propertyJson());

      expect(property.projectedPending(0), 30);
    });

    test('advances at the served rate', () {
      final property = Property.fromJson(propertyJson());

      expect(property.projectedPending(60), 34);
    });

    test('scales with the units owned', () {
      final property = Property.fromJson(propertyJson(quantity: 2, cap: 120));

      expect(property.projectedPending(60), 38);
    });

    test('floors a part-earned coin rather than drawing it early', () {
      final property = Property.fromJson(propertyJson());

      // Fourteen seconds at four a minute is 0.93 of a coin, which is not a coin.
      expect(property.projectedPending(14), 30);
    });

    test('never draws past the cap', () {
      final property = Property.fromJson(propertyJson());

      expect(property.projectedPending(3600), 60);
    });

    test('stays at zero for a property the player does not own', () {
      final property = Property.fromJson(
          propertyJson(quantity: 0, pending: 0, cap: 0, canCollect: false));

      expect(property.projectedPending(3600), 0);
    });
  });

  group('PropertyList.fromJson', () {
    test('reads the catalog with the cash and level it was gated against', () {
      final list = PropertyList.fromJson({
        'properties': [propertyJson()],
        'cash': 600,
        'level': 2,
      });

      expect(list.properties, hasLength(1));
      expect(list.properties.single.id, 'street-corner');
      expect(list.cash, 600);
      expect(list.level, 2);
    });
  });

  group('BuyPropertyResult.fromJson', () {
    test('reads the purchase and the post-action player', () {
      final result = BuyPropertyResult.fromJson({
        'propertyId': 'street-corner',
        'cashSpent': 500,
        'quantityOwned': 2,
        'pending': 30,
        'cap': 120,
        'player': meJson(cash: 100),
      });

      expect(result.propertyId, 'street-corner');
      expect(result.cashSpent, 500);
      expect(result.quantityOwned, 2);
      expect(result.pending, 30);
      expect(result.cap, 120);
      expect(result.player.cash, 100);
    });
  });

  group('CollectPropertyResult.fromJson', () {
    test('reads the takings and the post-action player', () {
      final result = CollectPropertyResult.fromJson({
        'propertyId': 'street-corner',
        'cashCollected': 30,
        'pending': 0,
        'cap': 60,
        'player': meJson(cash: 630),
      });

      expect(result.propertyId, 'street-corner');
      expect(result.cashCollected, 30);
      expect(result.pending, 0);
      expect(result.cap, 60);
      expect(result.player.cash, 630);
    });
  });
}
