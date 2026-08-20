import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/inventory_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps InventoryScreen with a bootstrapped AppState.
Future<AppState> pumpInventory(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: InventoryScreen(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

/// A catalog in which the player owns nothing.
const _nothingOwned = InventoryList(
  items: [kUnaffordableArmor, kLockedWeapon],
  loadout: kBareLoadout,
  cash: 100,
  level: 1,
);

void main() {
  group('InventoryScreen — holdings', () {
    testWidgets('lists only the items the player owns', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      // The same call feeds the Store, which shows all three. The Inventory is the owned view of it.
      expect(find.byKey(const Key('inv-card-brass-knuckles')), findsOneWidget);
      expect(find.byKey(const Key('inv-card-leather-jacket')), findsNothing);
      expect(find.byKey(const Key('inv-card-switchblade')), findsNothing);
    });

    testWidgets('loads the catalog on first frame', (tester) async {
      final api = FakeApiClient();
      await pumpInventory(tester, api);

      expect(api.inventoryCalls, 1);
    });

    testWidgets('shows an empty state when nothing is owned', (tester) async {
      await pumpInventory(tester, FakeApiClient(inventoryResult: _nothingOwned));

      expect(find.byKey(const Key('inv-empty')), findsOneWidget);
    });
  });

  group('InventoryScreen — effective stats', () {
    testWidgets('shows the effective attack and defense', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      expect(
        find.descendant(
          of: find.byKey(const Key('inv-attack')),
          matching: find.textContaining('10'),
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('inv-defense')), findsOneWidget);
    });

    testWidgets('equipping raises the effective attack', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      // This is the CP3 observation: base 10 plus the weapon's 4.
      expect(
        find.descendant(
          of: find.byKey(const Key('inv-attack')),
          matching: find.textContaining('14'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('separates what the player owns from what the gear adds', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      // "Attack 14" alone cannot tell a player that four of it comes off on unequip.
      expect(find.textContaining('10 + 4'), findsOneWidget);
    });
  });

  group('InventoryScreen — equipping', () {
    testWidgets('an owned, unworn item offers an equip button', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      expect(find.byKey(const Key('equip-item-brass-knuckles')), findsOneWidget);
      expect(find.byKey(const Key('unequip-item-brass-knuckles')), findsNothing);
    });

    testWidgets('tapping equip sends the request', (tester) async {
      final api = FakeApiClient();
      await pumpInventory(tester, api);

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(api.equipCalls, ['brass-knuckles']);
    });

    testWidgets('a worn item offers unequip instead', (tester) async {
      await pumpInventory(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('unequip-item-brass-knuckles')), findsOneWidget);
      expect(find.byKey(const Key('equip-item-brass-knuckles')), findsNothing);
    });

    testWidgets('tapping unequip sends the request', (tester) async {
      final api = FakeApiClient();
      await pumpInventory(tester, api);

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('unequip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(api.unequipCalls, ['brass-knuckles']);
    });

    testWidgets('a refusal shows a banner rather than an error screen', (tester) async {
      await pumpInventory(tester, FakeApiClient(refuseItemActionWith: 'alreadyEquipped'));

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.byKey(const Key('inv-error')), findsNothing);
    });
  });

  group('InventoryScreen — errors', () {
    testWidgets('a catalog failure shows an error and a retry', (tester) async {
      await pumpInventory(tester, FakeApiClient(throwOnInventory: true));

      expect(find.byKey(const Key('inv-error')), findsOneWidget);
      expect(find.byKey(const Key('inv-retry')), findsOneWidget);
    });

    testWidgets('retry re-requests the catalog', (tester) async {
      final api = FakeApiClient(throwOnInventory: true);
      await pumpInventory(tester, api);

      await tester.tap(find.byKey(const Key('inv-retry')));
      await tester.pumpAndSettle();

      expect(api.inventoryCalls, 2);
    });
  });
}
