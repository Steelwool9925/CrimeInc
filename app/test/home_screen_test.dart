import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/home_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Widget _wrap(AppState state, {VoidCallback? onChangeServer}) => MaterialApp(
      home: HomeScreen(appState: state, onChangeServer: onChangeServer ?? () {}),
    );

AppState _readyState() {
  final api = FakeApiClient();
  final store = InMemoryTokenStore();
  return AppState(apiClient: api, tokenStore: store);
}

Future<AppState> _bootstrapped(WidgetTester tester) async {
  final state = _readyState();
  await state.bootstrap();
  return state;
}

void main() {
  group('HomeScreen — loading state', () {
    testWidgets('shows CircularProgressIndicator while loading',
        (tester) async {
      final api = FakeApiClient();
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      // Do NOT bootstrap — stays in loading

      await tester.pumpWidget(_wrap(state));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });
  });

  group('HomeScreen — error state', () {
    testWidgets('shows error message and Retry button', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.textContaining('error'), findsWidgets);
      expect(find.widgetWithText(ElevatedButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows a Change Server button next to Retry', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // A stuck server address (e.g. a friend's tunnel URL that has since rotated, per
      // docs/hosting-a-friend.md) is otherwise a dead end: Retry can only ever hit the same
      // unreachable host again, with no in-app way back to ServerAddressScreen.
      expect(find.widgetWithText(OutlinedButton, 'Change Server'), findsOneWidget);
    });

    testWidgets('Change Server button calls onChangeServer', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      var called = false;
      await tester.pumpWidget(_wrap(state, onChangeServer: () => called = true));
      await tester.pump();

      await tester.tap(find.widgetWithText(OutlinedButton, 'Change Server'));
      await tester.pump();

      expect(called, isTrue);
    });
  });

  group('HomeScreen — ready state', () {
    testWidgets('shows cash value and label', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // Cash label
      expect(find.text('Cash'), findsOneWidget);
      // Cash value — 12345 from FakeApiClient default
      expect(find.text('12345'), findsOneWidget);
    });

    testWidgets('shows energy meter with 7 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Energy'), findsOneWidget);
      expect(find.text('7 / 10'), findsOneWidget);
    });

    testWidgets('shows stamina meter with 5 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Stamina'), findsOneWidget);
      expect(find.text('5 / 10'), findsOneWidget);
    });

    testWidgets('shows health meter with 10 / 10', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.text('Health'), findsOneWidget);
      expect(find.text('10 / 10'), findsOneWidget);
    });

    testWidgets('shows three LinearProgressIndicators', (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.byType(LinearProgressIndicator), findsNWidgets(3));
    });

    testWidgets('shows secondsUntilNextPoint for non-full meters',
        (tester) async {
      final state = await _bootstrapped(tester);

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      // Energy has secondsUntilNextPoint: 42 — under a minute, so plain seconds.
      expect(find.text('+1 in 42s'), findsOneWidget);
      // Stamina has secondsUntilNextPoint: 120. T4.2 formats anything over a minute as
      // minutes and zero-padded seconds; this read "+1 in 120s" before.
      expect(find.text('+1 in 2m 00s'), findsOneWidget);
      // Health is full — no countdown
      expect(find.textContaining('in '), findsNWidgets(2));
    });

    testWidgets('full meter does not show countdown text', (tester) async {
      final api = FakeApiClient(
        meResult: const Me(
          playerId: 'p1',
          energy: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          stamina: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          health: MeterState(
            current: 10,
            max: 10,
            regenSecondsPerPoint: 300,
          ),
          cash: 0,
          premiumCurrency: 0,
          level: 1,
          xp: 0,
          skillPoints: 0,
          attack: 10,
          defense: 10,
        ),
      );
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.textContaining('+1 in'), findsNothing);
    });

    testWidgets('Retry button triggers bootstrap again', (tester) async {
      final api = FakeApiClient(throwOnSession: true);
      final store = InMemoryTokenStore();
      final state = AppState(apiClient: api, tokenStore: store);
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pump();

      expect(find.widgetWithText(ElevatedButton, 'Retry'), findsOneWidget);
      await tester.tap(find.widgetWithText(ElevatedButton, 'Retry'));
      await tester.pump();

      expect(api.sessionCalls.length, greaterThan(1));
    });
  });

  group('HomeScreen — navigation', () {
    testWidgets('every live tile sorts above the "Coming soon" ones', (tester) async {
      // Order is load-bearing on a tall viewport, where only the first row or two is above the
      // fold. Market used to sit *below* the dead Crew tile, so a player had to scroll past a
      // feature that does not exist to reach one that does — and a first session now earns enough
      // to make the Market worth reaching. Pin the rule, not just today's arrangement: no
      // placeholder may precede a live destination.
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      const live = [
        Key('go-to-jobs'),
        Key('go-to-properties'),
        Key('go-to-store'),
        Key('go-to-inventory'),
        Key('go-to-profile'),
      ];
      const placeholders = [Key('go-to-crew'), Key('go-to-territory')];

      // Reading order: row first, then column. A live tile may share a row with a placeholder
      // (Rap Sheet sits left of Crew) so long as it comes first.
      double position(Key k) {
        final o = tester.getTopLeft(find.byKey(k));
        return o.dy * 10000 + o.dx;
      }

      final lastLive = live.map(position).reduce((a, b) => a > b ? a : b);
      final firstPlaceholder = placeholders.map(position).reduce((a, b) => a < b ? a : b);

      expect(firstPlaceholder, greaterThan(lastLive),
          reason: 'a "Coming soon" tile must never be reachable before a working one');
    });

    testWidgets('Market leads the second column and Inventory sits directly under it', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      Offset at(String key) => tester.getTopLeft(find.byKey(Key(key)));
      expect(at('go-to-store').dy, at('go-to-jobs').dy, reason: 'Market shares the first row with Jobs');
      expect(at('go-to-store').dx, greaterThan(at('go-to-jobs').dx));
      expect(at('go-to-inventory').dx, at('go-to-store').dx);
      expect(at('go-to-inventory').dy, greaterThan(at('go-to-store').dy));
      expect(at('go-to-properties').dy, at('go-to-inventory').dy);
    });

    testWidgets('the Inventory tile is labelled and opens the Inventory', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-inventory')));
      await tester.pumpAndSettle();
      expect(
        find.descendant(of: find.byKey(const Key('go-to-inventory')), matching: find.text('Inventory')),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('go-to-inventory')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('inv-card-brass-knuckles')), findsOneWidget);
    });

    // A catalog with nothing worth advising, so Home carries no prompt.
    InventoryList noAdvice() =>
        const InventoryList(items: [], loadout: kBareLoadout, cash: 12345, level: 1);

    Future<AppState> pumpPhone(WidgetTester tester, FakeApiClient api) async {
      tester.view.physicalSize = const Size(375, 667);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();
      return state;
    }

    // The bottom nav takes 56px of the 667, so what a player sees is the first 611.
    const visibleHeight = 667.0 - 56.0;

    testWidgets('on a 375x667 phone the Market and Inventory tiles sit above the fold', (tester) async {
      // The whole point of the dashboard is that a player finds the shop. With a card each for the
      // meters, cash and level, the tile grid began around y=550 and the Market name sat on the
      // fold.
      await pumpPhone(tester, FakeApiClient(inventoryResult: noAdvice()));

      final market = tester.getRect(find.byKey(const Key('go-to-store')));
      final inventory = tester.getRect(find.byKey(const Key('go-to-inventory')));
      expect(market.bottom, lessThanOrEqualTo(visibleHeight),
          reason: 'the whole Market tile must be on the first screen');
      expect(inventory.top, lessThan(visibleHeight),
          reason: 'the Inventory tile must at least begin on the first screen');
    });

    testWidgets('with an upgrade to announce, the prompt is above the fold and the Market tile starts there',
        (tester) async {
      // The prompt takes a row of its own, so the Market tile moves down by that much. The test
      // fonts are larger than the real ones, so this pins only what they can prove: the whole
      // prompt is on the first screen and the Market tile begins on it. Whether the icon and name
      // clear the fold is checked on a real 375x667 viewport.
      await pumpPhone(tester, FakeApiClient());

      final prompt = tester.getRect(find.byKey(const Key('home-upgrade-prompt')));
      final market = tester.getRect(find.byKey(const Key('go-to-store')));
      expect(prompt.bottom, lessThanOrEqualTo(visibleHeight));
      expect(market.top, lessThan(visibleHeight));
    });

    testWidgets('the prompt wraps a two-stat upgrade instead of cutting it off', (tester) async {
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
      await pumpPhone(
        tester,
        FakeApiClient(
          inventoryResult: const InventoryList(items: [worn, blade], loadout: kBareLoadout, cash: 12345, level: 2),
        ),
      );

      final gain = find.descendant(
        of: find.byKey(const Key('home-upgrade-prompt')),
        matching: find.textContaining('+1 defense'),
      );
      expect(gain, findsOneWidget);
      expect(tester.renderObject<RenderParagraph>(gain).didExceedMaxLines, isFalse);
    });

    testWidgets('offers a way into the Store', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      // Without this the screen exists but is unreachable, which is the same as not shipping
      // it: CP3 is a sequence a player performs, not a set of routes.
      expect(find.byKey(const Key('go-to-store')), findsOneWidget);
    });

    testWidgets('the Store button opens the Store', (tester) async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      // Home scrolls: with four destinations the later buttons sit below the fold on a small
      // viewport, so reaching them is a scroll the player makes too.
      await tester.ensureVisible(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('store-card-brass-knuckles')), findsOneWidget);
    });

    testWidgets('offers a way into Properties', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('go-to-properties')), findsOneWidget);
    });

    testWidgets('the Properties button opens Properties', (tester) async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-properties')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-properties')));

      // Not pumpAndSettle past this point: the Properties screen keeps a ticker running to draw
      // its counters filling, so there is no frame after which nothing is scheduled.
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('property-card-street-corner')), findsOneWidget);

      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    });

    testWidgets('the Crew tile opens the Crew coming-soon screen', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-crew')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-crew')));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'Crew'), findsOneWidget);
    });

    testWidgets('the Territory tile opens the Territory coming-soon screen',
        (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-territory')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-territory')));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(AppBar, 'Territory'), findsOneWidget);
    });

    testWidgets('the Rap Sheet tile opens the Profile screen', (tester) async {
      final state = AppState(
          apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();

      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-profile')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go-to-profile')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('profile-stat-cash')), findsOneWidget);
    });
  });

  group('HomeScreen — upgrade advice', () {
    testWidgets('names the upgrade, what it adds and what it costs', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      final prompt = find.byKey(const Key('home-upgrade-prompt'));
      expect(prompt, findsOneWidget);
      expect(find.descendant(of: prompt, matching: find.text('Upgrade available')), findsOneWidget);
      expect(find.descendant(of: prompt, matching: find.textContaining('Leather Jacket')), findsOneWidget);
      expect(find.descendant(of: prompt, matching: find.textContaining('+5 defense')), findsOneWidget);
      expect(find.descendant(of: prompt, matching: find.textContaining('120')), findsOneWidget);
    });

    testWidgets('puts the words and the dot on the Market tile, not the dot alone', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      final tile = find.byKey(const Key('go-to-store'));
      expect(find.descendant(of: tile, matching: find.text('Upgrade available')), findsOneWidget);
      expect(find.byKey(const Key('go-to-store-badge')), findsOneWidget);
    });

    testWidgets('says nothing when there is nothing to advise', (tester) async {
      final api = FakeApiClient(
        inventoryResult:
            const InventoryList(items: [], loadout: kBareLoadout, cash: 12345, level: 1),
      );
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('home-upgrade-prompt')), findsNothing);
      expect(find.byKey(const Key('go-to-store-badge')), findsNothing);
      expect(
        find.descendant(of: find.byKey(const Key('go-to-store')), matching: find.text('Upgrade available')),
        findsNothing,
      );
    });

    testWidgets('a live tile with a subtitle is not dimmed like a Coming-soon one', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      Color? subtitleColor(String key, String text) => tester
          .widget<Text>(find.descendant(of: find.byKey(Key(key)), matching: find.text(text)))
          .style
          ?.color;

      expect(subtitleColor('go-to-store', 'Upgrade available')!.a, 1.0);
      expect(subtitleColor('go-to-crew', 'Coming soon')!.a, lessThan(1.0));
    });

    testWidgets('tapping the prompt opens the Market', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('home-upgrade-prompt')));
      await tester.tap(find.byKey(const Key('home-upgrade-prompt')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('store-card-brass-knuckles')), findsOneWidget);
    });

    testWidgets('the prompt can be activated through the accessibility tree, not only by touch',
        (tester) async {
      final handle = tester.ensureSemantics();
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      final prompt = find.semantics.byLabel(RegExp('Upgrade available: Leather Jacket'));
      expect(prompt, findsOneWidget);

      tester.semantics.tap(prompt);
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('store-highlight-leather-jacket')), findsOneWidget);
      handle.dispose();
    });

    testWidgets('showing the advice costs no request beyond the bootstrap reads and the poll',
        (tester) async {
      final api = FakeApiClient();
      final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();
      final meCalls = api.meCalls;

      for (var i = 0; i < 5; i++) {
        await tester.pump(const Duration(seconds: 1));
      }
      // ignore: invalid_use_of_protected_member, invalid_use_of_visible_for_testing_member
      state.notifyListeners();
      await tester.pump();

      expect(find.byKey(const Key('home-upgrade-prompt')), findsOneWidget);
      expect(api.inventoryCalls, 1, reason: 'one catalog read at bootstrap, none for rendering');
      expect(api.meCalls, meCalls);
    });
  });

  group('HomeScreen — from the prompt to the item', () {
    testWidgets('the prompt opens the Market with the advised item marked and in view', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('home-upgrade-prompt')));
      await tester.tap(find.byKey(const Key('home-upgrade-prompt')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('store-highlight-leather-jacket')), findsOneWidget);
      expect(find.text('Your next upgrade'), findsOneWidget);
    });

    testWidgets('the Market tile opens the Market without singling an item out', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('go-to-store')));
      await tester.tap(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();

      expect(find.text('Your next upgrade'), findsNothing);
    });

    testWidgets('the dot clears once the Market has been visited, and the words stay', (tester) async {
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());
      await state.bootstrap();
      await tester.pumpWidget(_wrap(state));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('go-to-store-badge')), findsOneWidget);

      await tester.ensureVisible(find.byKey(const Key('go-to-store')));
      await tester.tap(find.byKey(const Key('go-to-store')));
      await tester.pumpAndSettle();
      await tester.pageBack();
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('go-to-store-badge')), findsNothing);
      expect(find.byKey(const Key('home-upgrade-prompt')), findsOneWidget);
      expect(
        find.descendant(of: find.byKey(const Key('go-to-store')), matching: find.text('Upgrade available')),
        findsOneWidget,
      );
    });
  });
}
