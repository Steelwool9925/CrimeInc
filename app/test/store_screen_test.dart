import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/store_screen.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/theme/pack_theme.dart';

import 'fakes.dart';

/// Pumps StoreScreen with a bootstrapped AppState.
Future<AppState> pumpStore(
  WidgetTester tester,
  FakeApiClient api, {
  String? highlightItemId,
  ThemeData? theme,
}) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(
    MaterialApp(
      theme: theme,
      home: StoreScreen(appState: state, highlightItemId: highlightItemId),
    ),
  );
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

      // One read at bootstrap (so Home can advise an upgrade), one when the Market opens.
      expect(api.inventoryCalls, 2);
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

    testWidgets('the Inventory control says what it is in words, not only an icon', (tester) async {
      await pumpStore(tester, FakeApiClient());

      // A tooltip never appears on a phone tap, so the name has to be on the control itself.
      expect(
        find.descendant(of: find.byKey(const Key('go-to-inventory')), matching: find.text('Inventory')),
        findsOneWidget,
      );
      expect(
        find.descendant(of: find.byKey(const Key('go-to-inventory')), matching: find.byIcon(Icons.backpack_outlined)),
        findsOneWidget,
      );
    });

    testWidgets('on a very narrow screen the cash moves under the title so the title keeps its room',
        (tester) async {
      tester.view.physicalSize = const Size(320, 568);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      await pumpStore(tester, FakeApiClient());

      final bar = tester.getRect(find.byType(AppBar));
      final cash = tester.getRect(find.byKey(const Key('store-cash')));
      final title = tester.getRect(find.descendant(of: find.byType(AppBar), matching: find.text('Market')));
      expect(cash.top, greaterThan(title.bottom), reason: 'cash sits on its own line, not beside the title');
      expect(cash.bottom, lessThanOrEqualTo(bar.bottom), reason: 'still part of the bar');
      expect(
        find.descendant(of: find.byKey(const Key('go-to-inventory')), matching: find.text('Inventory')),
        findsOneWidget,
        reason: 'the Inventory control keeps its word',
      );
    });

    testWidgets('on an ordinary phone the cash stays beside the title', (tester) async {
      tester.view.physicalSize = const Size(375, 667);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      await pumpStore(tester, FakeApiClient());

      final title = tester.getRect(find.descendant(of: find.byType(AppBar), matching: find.text('Market')));
      final cash = tester.getRect(find.byKey(const Key('store-cash')));
      expect(cash.top, lessThan(title.bottom));
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
      expect(api.inventoryCalls, 2, reason: 'bootstrap read plus the Market opening');

      await tester.tap(find.byKey(const Key('store-retry')));
      await tester.pumpAndSettle();

      expect(api.inventoryCalls, 3);
    });
  });

  group('StoreScreen — highlighted item', () {
    // A catalog long enough that the last row starts well below the first screen.
    InventoryItem row(int n, {int quantity = 0}) => InventoryItem(
          id: 'item-$n',
          name: 'Item $n',
          description: 'Filler to give the row some height.',
          slot: 'weapon',
          levelRequirement: 1,
          price: 10 * n,
          attack: n,
          defense: 0,
          quantity: quantity,
          equipped: false,
          canBuy: true,
          canEquip: quantity > 0,
        );

    FakeApiClient longCatalog({int ownedIndex = -1}) => FakeApiClient(
          inventoryResult: InventoryList(
            items: [for (var i = 1; i <= 14; i++) row(i, quantity: i == ownedIndex ? 1 : 0)],
            loadout: kBareLoadout,
            cash: 12345,
            level: 1,
          ),
        );

    testWidgets('marks the item it was asked to, in words', (tester) async {
      await pumpStore(tester, FakeApiClient(), highlightItemId: 'leather-jacket');

      final marker = find.byKey(const Key('store-highlight-leather-jacket'));
      expect(marker, findsOneWidget);
      expect(find.descendant(of: marker, matching: find.text('Your next upgrade')), findsOneWidget);
      expect(find.byKey(const Key('store-highlight-brass-knuckles')), findsNothing);
    });

    testWidgets('under the app theme the marked card also carries an accent border', (tester) async {
      final theme = packTheme(null);
      await pumpStore(tester, FakeApiClient(), highlightItemId: 'leather-jacket', theme: theme);

      final marked = tester.widget<Card>(find.byKey(const Key('store-card-leather-jacket')));
      final side = (marked.shape as RoundedRectangleBorder).side;
      expect(side.color, theme.colorScheme.primary);
      expect(side.width, 3);

      final other = tester.widget<Card>(find.byKey(const Key('store-card-brass-knuckles')));
      expect(other.shape, isNull);
    });

    testWidgets('says what the marked item adds over the one worn, so its stats are not read as the gain', (tester) async {
      const worn = InventoryItem(
        id: 'brass-knuckles',
        name: 'Brass Knuckles',
        description: '',
        slot: 'weapon',
        levelRequirement: 1,
        price: 75,
        attack: 4,
        defense: 0,
        quantity: 1,
        equipped: true,
        canBuy: true,
        canEquip: true,
      );
      const blade = InventoryItem(
        id: 'switchblade',
        name: 'Switchblade',
        description: '',
        slot: 'weapon',
        levelRequirement: 2,
        price: 180,
        attack: 7,
        defense: 1,
        quantity: 0,
        equipped: false,
        canBuy: true,
        canEquip: false,
      );
      await pumpStore(
        tester,
        FakeApiClient(
          inventoryResult: const InventoryList(items: [worn, blade], loadout: kBareLoadout, cash: 12345, level: 2),
        ),
        highlightItemId: 'switchblade',
      );

      expect(
        find.descendant(
          of: find.byKey(const Key('store-card-switchblade')),
          matching: find.text('+3 attack, +1 defense more than your Brass Knuckles'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('says nothing extra when the marked item fills an empty slot', (tester) async {
      await pumpStore(tester, FakeApiClient(), highlightItemId: 'leather-jacket');

      expect(find.textContaining('more than your'), findsNothing);
    });

    testWidgets('marks nothing when no item was asked for', (tester) async {
      await pumpStore(tester, FakeApiClient());

      expect(find.text('Your next upgrade'), findsNothing);
    });

    testWidgets('marks nothing for an id the catalog does not have', (tester) async {
      await pumpStore(tester, FakeApiClient(), highlightItemId: 'no-such-item');

      expect(find.text('Your next upgrade'), findsNothing);
    });

    testWidgets('brings an item from far down the list into view', (tester) async {
      await pumpStore(tester, longCatalog(), highlightItemId: 'item-14');

      final screen = tester.view.physicalSize / tester.view.devicePixelRatio;
      final card = tester.getRect(find.byKey(const Key('store-card-item-14')));
      expect(card.top, greaterThanOrEqualTo(0));
      expect(card.bottom, lessThanOrEqualTo(screen.height),
          reason: 'the whole recommended card must be on screen without scrolling');
    });

    testWidgets('without a highlight the list starts at the top', (tester) async {
      await pumpStore(tester, longCatalog());

      expect(tester.getRect(find.byKey(const Key('store-card-item-1'))).top, lessThan(200));
    });

    testWidgets('an item the player already owns is not recommended', (tester) async {
      await pumpStore(tester, longCatalog(ownedIndex: 3), highlightItemId: 'item-3');

      expect(find.text('Your next upgrade'), findsNothing);
    });

    testWidgets('does not pull the list back after the player has scrolled away', (tester) async {
      final state = await pumpStore(tester, longCatalog(), highlightItemId: 'item-14');

      await tester.drag(find.byType(SingleChildScrollView), const Offset(0, 4000));
      await tester.pumpAndSettle();
      // Any state change rebuilds the list; the reveal must not run again when it does.
      await state.loadInventory();
      await tester.pumpAndSettle();

      expect(tester.getRect(find.byKey(const Key('store-card-item-1'))).top, greaterThanOrEqualTo(0),
          reason: 'a rebuild must not scroll the list back to the recommendation');
    });

    testWidgets('opening the Market counts as having seen the upgrade', (tester) async {
      final state = await pumpStore(tester, FakeApiClient());

      expect(state.upgradeBadgeVisible, isFalse);
    });
  });
}
