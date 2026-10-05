import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/property_gating.dart';

/// One catalog row as GET /properties sends it.
Map<String, dynamic> propertyJson({
  int quantity = 1,
  int pending = 30,
  int cap = 60,
  bool canBuy = true,
  String? buyReason,
  bool canCollect = true,
  String? collectReason,
  int? maxUnits,
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
      'maxUnits': maxUnits,
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

  group('the unit ceiling', () {
    test('reads the ceiling the server sends', () {
      expect(Property.fromJson(propertyJson(maxUnits: 3)).maxUnits, 3);
    });

    test('a property with no ceiling reads as null, not zero', () {
      expect(Property.fromJson(propertyJson()).maxUnits, isNull);
    });

    test('says how many are held against how many may be', () {
      final owned = Property.fromJson(propertyJson(quantity: 2, maxUnits: 3));
      expect(propertyUnitsLabel(owned), 'Owned: 2 of 3');
    });

    test('says what a player who owns none may hold', () {
      final none = Property.fromJson(propertyJson(quantity: 0, maxUnits: 3));
      expect(propertyUnitsLabel(none), 'You can hold up to 3');
    });

    test('with no ceiling it says only what is owned, and nothing when none are', () {
      expect(propertyUnitsLabel(Property.fromJson(propertyJson(quantity: 2))), 'Owned: 2');
      expect(propertyUnitsLabel(Property.fromJson(propertyJson(quantity: 0))), isNull);
    });

    test('a buy refused at the limit says so', () {
      final atLimit = Property.fromJson(propertyJson(
          quantity: 3, maxUnits: 3, canBuy: false, buyReason: 'limit'));
      expect(propertyBuyLockLabel(atLimit), 'You hold the most you can');
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

  group('accrualWindowLabel', () {
    // Untested until now, and the rebalance moved every shipped cap into the hours branch:
    // the pack used to run from 15 minutes to a day, and now runs from 8 hours to a day.
    test('renders every cap the shipped pack actually uses', () {
      expect(accrualWindowLabel(480), '8 hours');
      expect(accrualWindowLabel(720), '12 hours');
      expect(accrualWindowLabel(1440), '24 hours');
    });

    test('still handles the windows the schema permits but the pack no longer uses', () {
      // content/crime/README.md allows 1..43,200 minutes, so a future pack may use these again.
      expect(accrualWindowLabel(15), '15 min');
      expect(accrualWindowLabel(59), '59 min');
      expect(accrualWindowLabel(60), '1 hour');
      // Not a whole number of hours, so minutes is the largest unit that stays exact —
      // "1.5 hours" would be a rounding the player cannot check against the counter.
      expect(accrualWindowLabel(90), '90 min');
    });
  });

  group('propertyWindowCash', () {
    // Derived on the client because the server sends cap: 0 for a property the player does not
    // own. It must agree with the domain definition - rate x window, floored to whole cash - or
    // the card promises a figure that never accrues.
    Property p(int perHour, int minutes) => Property(
          id: 'x',
          name: 'X',
          description: '',
          levelRequirement: 1,
          price: 1,
          incomePerHour: perHour,
          accrualCapMinutes: minutes,
          quantity: 0,
          pending: 0,
          cap: 0,
          canBuy: true,
          canCollect: false,
        );

    test('is exact where the window divides evenly', () {
      expect(propertyWindowCash(p(240, 480)), 1920);
      expect(propertyWindowCash(p(3600, 1440)), 86400);
    });

    test('floors where it does not, as the domain does', () {
      // 100 an hour over 100 minutes is 166.67: the player can be paid 166, never 167.
      expect(propertyWindowCash(p(100, 100)), 166);
    });

    test('appears in the income line, per unit, whether or not the player owns any', () {
      expect(propertyIncomeLabel(p(240, 480), 'Cash'),
          'Cash 240 an hour  \u00b7  fills in 8 hours  \u00b7  holds up to 1920');
    });
  });
}
