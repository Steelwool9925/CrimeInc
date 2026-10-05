import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/store_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps StoreScreen with a bootstrapped AppState.
Future<AppState> pumpStore(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: StoreScreen(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

void main() {
  group('StoreScreen — owned but not worn', () {
    // Buying does not equip, and gear that is not worn changes nothing: not the player's attack,
    // not which jobs unlock. The only way to equip used to be an unlabelled app-bar icon two
    // screens away, so the common outcome was a player spending their first cash and seeing no
    // effect whatsoever. The Market card has to say so, and offer the fix in place.

    testWidgets('an owned, unworn item says it is not worn and offers Equip', (tester) async {
      await pumpStore(tester, FakeApiClient());

      final owned = tester.widget<Text>(find.byKey(const Key('store-owned-brass-knuckles')));
      expect(owned.data, contains('not worn'));
      expect(find.byKey(const Key('equip-item-brass-knuckles')), findsOneWidget);
    });

    testWidgets('equipping from the Market card equips that item', (tester) async {
      final api = FakeApiClient();
      await pumpStore(tester, api);

      await tester.tap(find.byKey(const Key('equip-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(api.equipCalls, ['brass-knuckles']);
    });

    testWidgets('an item the player does not own offers no Equip', (tester) async {
      await pumpStore(tester, FakeApiClient());

      // Switchblade is in the catalogue but unowned, so there is nothing to equip.
      expect(find.byKey(const Key('equip-item-switchblade')), findsNothing);
      expect(find.byKey(const Key('store-owned-switchblade')), findsNothing);
    });
  });

  group('StoreScreen — catalog', () {
    testWidgets('renders a card per catalog item, owned or not', (tester) async {
      await pumpStore(tester, FakeApiClient());

      // The store sells the whole catalog: an item the player already owns still needs its price
      // shown when a second copy is on offer.
      expect(find.byKey(const Key('store-card-brass-knuckles')), findsOneWidget);
      expect(find.byKey(const Key('store-card-leather-jacket')), findsOneWidget);
      expect(find.byKey(const Key('store-card-switchblade')), findsOneWidget);
    });

    testWidgets('loads the catalog on first frame', (tester) async {
      final api = FakeApiClient();
      await pumpStore(tester, api);

      expect(api.inventoryCalls, 1);
    });

    testWidgets('shows the price and the stat an item moves', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(find.textContaining('75'), findsWidgets);
      expect(find.textContaining('+4 Attack'), findsOneWidget);
      expect(find.textContaining('+5 Defense'), findsOneWidget);
    });

    testWidgets('shows the cash the gating was computed against', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(find.byKey(const Key('store-cash')), findsOneWidget);
    });
  });

  group('StoreScreen — gating', () {
    testWidgets('an affordable item has an enabled buy button', (tester) async {
      await pumpStore(tester, FakeApiClient());

      final button = tester.widget<ElevatedButton>(
          find.byKey(const Key('buy-item-brass-knuckles')));

      expect(button.onPressed, isNotNull);
    });

    testWidgets('an unaffordable item is disabled and says why', (tester) async {
      await pumpStore(tester, FakeApiClient());

      final button = tester.widget<ElevatedButton>(
          find.byKey(const Key('buy-item-leather-jacket')));

      expect(button.onPressed, isNull);
      expect(find.byKey(const Key('store-lock-leather-jacket')), findsOneWidget);
    });

    testWidgets('a level-locked item names the level it needs', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(
        find.descendant(
          of: find.byKey(const Key('store-card-switchblade')),
          matching: find.textContaining('level 2'),
        ),
        findsOneWidget,
      );
    });
  });

  group('StoreScreen — buying', () {
    testWidgets('tapping buy sends the purchase', (tester) async {
      final api = FakeApiClient();
      await pumpStore(tester, api);

      await tester.tap(find.byKey(const Key('buy-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(api.buyCalls, ['brass-knuckles']);
    });

    testWidgets('the cash on screen follows the purchase', (tester) async {
      final api = FakeApiClient();
      await pumpStore(tester, api);

      await tester.tap(find.byKey(const Key('buy-item-brass-knuckles')));
      await tester.pumpAndSettle();

      // 100 less the knuckles' 75, from the server's post-action player.
      final cash = tester.widget<Text>(find.byKey(const Key('store-cash')));
      expect(cash.data, contains('25'));
    });

    testWidgets('a refusal shows a banner rather than an error screen', (tester) async {
      await pumpStore(tester, FakeApiClient(refuseItemActionWith: 'cash'));

      await tester.tap(find.byKey(const Key('buy-item-brass-knuckles')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.byKey(const Key('store-error')), findsNothing);
    });
  });

  group('StoreScreen — navigation', () {
    testWidgets('the AppBar shows Market, not Store', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(find.text('Market'), findsOneWidget);
      expect(find.text('Store'), findsNothing);
    });

    testWidgets('offers a way into the Inventory', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(find.byKey(const Key('go-to-inventory')), findsOneWidget);
    });

    testWidgets('the Inventory action opens the Inventory', (tester) async {
      final api = FakeApiClient();
      await pumpStore(tester, api);

      await tester.tap(find.byKey(const Key('go-to-inventory')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('inv-card-brass-knuckles')), findsOneWidget);
    });
  });

  group('StoreScreen — errors', () {
    testWidgets('a catalog failure shows an error and a retry', (tester) async {
      await pumpStore(tester, FakeApiClient(throwOnInventory: true));

      expect(find.byKey(const Key('store-error')), findsOneWidget);
      expect(find.byKey(const Key('store-retry')), findsOneWidget);
    });

    testWidgets('retry re-requests the catalog', (tester) async {
      final api = FakeApiClient(throwOnInventory: true);
      await pumpStore(tester, api);
      expect(api.inventoryCalls, 1);

      await tester.tap(find.byKey(const Key('store-retry')));
      await tester.pumpAndSettle();

      expect(api.inventoryCalls, 2);
    });
  });
}
