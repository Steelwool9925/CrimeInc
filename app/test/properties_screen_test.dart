import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/properties_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps PropertiesScreen with a bootstrapped AppState.
///
/// [refreshInterval] is pushed far out by default so a test that advances the clock to watch the
/// counter fill is not interrupted by a re-read serving the original figure again. The tests that
/// are about the re-read set it themselves.
///
/// `pump` rather than `pumpAndSettle` throughout: the screen keeps a periodic ticker running, so
/// there is never a frame after which nothing is scheduled.
Future<AppState> pumpProperties(
  WidgetTester tester,
  FakeApiClient api, {
  Duration refreshInterval = const Duration(hours: 1),
}) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(
    home: PropertiesScreen(appState: state, refreshInterval: refreshInterval),
  ));
  await tester.pump();
  await tester.pump();

  // Tear the tree down inside the test's own zone, so the ticker is cancelled before the binding
  // checks for timers still pending.
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));

  return state;
}

/// Settles the futures a tap starts without waiting for a quiet frame that never comes.
Future<void> settle(WidgetTester tester) async {
  await tester.pump();
  await tester.pump();
  await tester.pump();
}

/// Scrolls the list far enough to reach the cards below the fold.
///
/// Three property cards do not fit an 800px test viewport, and `ListView.builder` does not build
/// what it has not scrolled to — so the last card is unreachable until the list moves, which is the
/// same scroll a player makes.
Future<void> scrollToBottom(WidgetTester tester) async {
  await tester.drag(find.byType(ListView), const Offset(0, -600));
  await tester.pump();
}

void main() {
  group('PropertiesScreen — catalog', () {
    testWidgets('renders a card per catalog property, held or not', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      // One list serves both halves of the screen: what you own and what you could buy.
      expect(find.byKey(const Key('property-card-street-corner')), findsOneWidget);
      expect(find.byKey(const Key('property-card-pawn-shop')), findsOneWidget);

      await scrollToBottom(tester);
      expect(find.byKey(const Key('property-card-car-wash')), findsOneWidget);
    });

    testWidgets('loads the catalog on first frame', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      expect(api.propertiesCalls, 1);
    });

    testWidgets('shows the price and the rate it earns', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      expect(
        find.descendant(
          of: find.byKey(const Key('property-card-street-corner')),
          matching: find.textContaining('500'),
        ),
        findsOneWidget,
      );
      final income = tester
          .widget<Text>(find.byKey(const Key('property-income-street-corner')));
      expect(income.data, contains('240'));
    });

    testWidgets('names the window a holding fills in', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final income = tester
          .widget<Text>(find.byKey(const Key('property-income-street-corner')));
      expect(income.data, contains('15'));
    });

    testWidgets('shows the cash the gating was computed against', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      expect(find.byKey(const Key('properties-cash')), findsOneWidget);
    });
  });

  group('PropertiesScreen — holdings', () {
    testWidgets('a held property shows how many units are owned', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      expect(
        find.descendant(
          of: find.byKey(const Key('property-card-street-corner')),
          matching: find.textContaining('Owned: 1'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('a held property shows what it has banked against its cap', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final pending =
          tester.widget<Text>(find.byKey(const Key('property-pending-street-corner')));

      expect(pending.data, contains('30'));
      expect(pending.data, contains('60'));
    });

    testWidgets('an unheld property draws no counter at all', (tester) async {
      await pumpProperties(tester, FakeApiClient());
      await scrollToBottom(tester);

      // Scrolled into view first: an unbuilt card would satisfy findsNothing whatever the screen
      // does, and prove nothing.
      expect(find.byKey(const Key('property-card-car-wash')), findsOneWidget);

      // Cap is zero until a unit is bought, and a bar drawn against a ceiling the player has not
      // bought into would be a bar that can never move.
      expect(find.byKey(const Key('property-pending-car-wash')), findsNothing);
    });
  });

  group('PropertiesScreen — buying', () {
    testWidgets('an affordable property has an enabled buy button', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final button = tester.widget<ElevatedButton>(
          find.byKey(const Key('buy-property-street-corner')));

      expect(button.onPressed, isNotNull);
    });

    testWidgets('an unaffordable property is disabled and says why', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final button = tester
          .widget<ElevatedButton>(find.byKey(const Key('buy-property-pawn-shop')));

      expect(button.onPressed, isNull);
      expect(find.byKey(const Key('property-buy-lock-pawn-shop')), findsOneWidget);
    });

    testWidgets('a level-locked property names the level it needs', (tester) async {
      await pumpProperties(tester, FakeApiClient());
      await scrollToBottom(tester);

      final lock = tester
          .widget<Text>(find.byKey(const Key('property-buy-lock-car-wash')));
      expect(lock.data, contains('level 4'));
    });

    testWidgets('tapping buy sends the purchase', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.tap(find.byKey(const Key('buy-property-street-corner')));
      await settle(tester);

      expect(api.buyPropertyCalls, ['street-corner']);
    });

    testWidgets('the cash on screen follows the purchase', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.tap(find.byKey(const Key('buy-property-street-corner')));
      await settle(tester);

      // 600 less the corner's 500, from the server's post-action player.
      final cash = tester.widget<Text>(find.byKey(const Key('properties-cash')));
      expect(cash.data, contains('100'));
    });
  });

  group('PropertiesScreen — collecting', () {
    testWidgets('a property with takings has an enabled collect button', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final button = tester.widget<FilledButton>(
          find.byKey(const Key('collect-property-street-corner')));

      expect(button.onPressed, isNotNull);
    });

    testWidgets('an empty counter is disabled and says why', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      final button = tester.widget<FilledButton>(
          find.byKey(const Key('collect-property-pawn-shop')));

      expect(button.onPressed, isNull);
      expect(find.byKey(const Key('property-collect-lock-pawn-shop')), findsOneWidget);
    });

    testWidgets('a property the player does not hold offers no collect', (tester) async {
      await pumpProperties(tester, FakeApiClient());
      await scrollToBottom(tester);

      expect(find.byKey(const Key('property-card-car-wash')), findsOneWidget);
      expect(find.byKey(const Key('collect-property-car-wash')), findsNothing);
    });

    testWidgets('tapping collect sends the collection', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.tap(find.byKey(const Key('collect-property-street-corner')));
      await settle(tester);

      expect(api.collectPropertyCalls, ['street-corner']);
    });

    testWidgets('the cash on screen follows the collection', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.tap(find.byKey(const Key('collect-property-street-corner')));
      await settle(tester);

      // 600 plus the corner's 30 takings — the rise CP3 is watching for.
      final cash = tester.widget<Text>(find.byKey(const Key('properties-cash')));
      expect(cash.data, contains('630'));
    });

    testWidgets('the counter empties once collected', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.tap(find.byKey(const Key('collect-property-street-corner')));
      await settle(tester);

      final button = tester.widget<FilledButton>(
          find.byKey(const Key('collect-property-street-corner')));
      expect(button.onPressed, isNull);
    });
  });

  group('PropertiesScreen — the counter filling', () {
    testWidgets('the banked figure climbs between reads', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      await tester.pump(const Duration(seconds: 60));

      // One unit of the corner earns four a minute, so a minute after the read that served 30 the
      // screen draws 34 — without having asked the server again.
      final pending =
          tester.widget<Text>(find.byKey(const Key('property-pending-street-corner')));
      expect(pending.data, contains('34'));
    });

    testWidgets('it climbs without a second request', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api);

      await tester.pump(const Duration(seconds: 60));

      expect(api.propertiesCalls, 1);
    });

    testWidgets('it stops at the cap', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      await tester.pump(const Duration(minutes: 30));

      final pending =
          tester.widget<Text>(find.byKey(const Key('property-pending-street-corner')));
      expect(pending.data, contains('60'));
    });

    testWidgets('an empty holding still fills toward its own cap', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      // The pawn shop banked nothing at the read, but it is owned and earning: 300 an hour is five
      // a minute.
      await tester.pump(const Duration(seconds: 60));

      final pending =
          tester.widget<Text>(find.byKey(const Key('property-pending-pawn-shop')));
      expect(pending.data, contains('5'));
    });

    testWidgets('the projection never enables a button the server refused', (tester) async {
      await pumpProperties(tester, FakeApiClient());

      await tester.pump(const Duration(seconds: 60));

      // The pawn shop's counter is drawn climbing, but whether it may be collected stays the
      // server's answer until the next read says otherwise.
      final button = tester.widget<FilledButton>(
          find.byKey(const Key('collect-property-pawn-shop')));
      expect(button.onPressed, isNull);
    });
  });

  group('PropertiesScreen — refreshing', () {
    testWidgets('re-reads the catalog on its refresh interval', (tester) async {
      final api = FakeApiClient();
      await pumpProperties(tester, api,
          refreshInterval: const Duration(seconds: 10));

      await tester.pump(const Duration(seconds: 10));
      await settle(tester);

      // The projection can draw the counter climbing, but only the server can say when collecting
      // becomes allowed, so the screen keeps asking.
      expect(api.propertiesCalls, 2);
    });
  });

  group('PropertiesScreen — refusals and errors', () {
    testWidgets('a refusal shows a banner rather than an error screen', (tester) async {
      await pumpProperties(
          tester, FakeApiClient(refusePropertyActionWith: 'cash'));

      await tester.tap(find.byKey(const Key('buy-property-street-corner')));
      await settle(tester);

      expect(find.byKey(const Key('action-message')), findsOneWidget);
      expect(find.byKey(const Key('properties-error')), findsNothing);
    });

    testWidgets('a catalog failure shows an error and a retry', (tester) async {
      await pumpProperties(tester, FakeApiClient(throwOnProperties: true));

      expect(find.byKey(const Key('properties-error')), findsOneWidget);
      expect(find.byKey(const Key('properties-retry')), findsOneWidget);
    });

    testWidgets('retry re-requests the catalog', (tester) async {
      final api = FakeApiClient(throwOnProperties: true);
      await pumpProperties(tester, api);
      expect(api.propertiesCalls, 1);

      await tester.tap(find.byKey(const Key('properties-retry')));
      await settle(tester);

      expect(api.propertiesCalls, 2);
    });
  });

  group('PropertiesScreen — what the card says before and after buying', () {
    void tallSurface(WidgetTester tester) {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
    }

    testWidgets('says what a full window holds, before the player has bought one',
        (tester) async {
      // "240 an hour, fills in 8 hours" made the player multiply to learn what a night is worth,
      // and the answer only appeared after buying, as the right-hand figure of the owned meter.
      // The decision to buy is made before that, so the payoff has to be on the card.
      tallSurface(tester);
      await pumpProperties(tester, FakeApiClient());

      final owned = tester
          .widget<Text>(find.byKey(const Key('property-income-street-corner')))
          .data!;
      // 240 an hour over a 15-minute window, floored: 60.
      expect(owned, contains('holds up to 60'));

      // The Car Wash is unowned, so the server sends cap: 0 for it. The figure must come from the
      // rate and the window, not from that zero: 640 an hour over 240 minutes is 2560.
      final unowned =
          tester.widget<Text>(find.byKey(const Key('property-income-car-wash'))).data!;
      expect(unowned, contains('holds up to 2560'));
    });

    testWidgets('each refusal names the button it explains', (tester) async {
      // Two lines used to sit side by side, unlabelled, to the left of two buttons, so either
      // could have been about either button.
      tallSurface(tester);
      await pumpProperties(tester, FakeApiClient());

      // The owned Pawn Shop is refused on both counts at once.
      expect(find.text('Buy: Costs more than you have'), findsOneWidget);
      expect(find.text('Collect: Nothing on the counter yet'), findsOneWidget);
    });

    testWidgets('the card keeps its height whether or not a refusal is showing', (tester) async {
      // Which reason lines appear changes as cash rises and as the meter fills, and each change
      // used to move every card below it by a line - the Do job shift, in miniature.
      Future<double> heightOf(Property p) async {
        await tester.pumpWidget(const SizedBox.shrink());
        await pumpProperties(
          tester,
          FakeApiClient(
            propertiesResult: PropertyList(properties: [p], cash: 600, level: 2),
          ),
        );
        return tester.getSize(find.byKey(Key('property-card-${p.id}'))).height;
      }

      // Owned: nothing refused, versus both buttons refused.
      final unrefused = await heightOf(kCollectableProperty);
      final refused = await heightOf(const Property(
        id: 'street-corner',
        name: 'Street Corner',
        description: 'A patch of pavement nobody works without your say-so.',
        levelRequirement: 1,
        price: 500,
        incomePerHour: 240,
        accrualCapMinutes: 15,
        quantity: 1,
        pending: 0,
        cap: 60,
        canBuy: false,
        buyUnavailableReason: 'cash',
        canCollect: false,
        collectUnavailableReason: 'nothingToCollect',
      ));
      expect(refused, unrefused,
          reason: 'an owned card moved when its refusals appeared or went');

      // Unowned: affordable, versus not.
      final affordable = await heightOf(const Property(
        id: 'car-wash',
        name: 'Car Wash',
        description: 'Twelve dollars a wash and a great deal more than twelve dollars a day.',
        levelRequirement: 4,
        price: 6000,
        incomePerHour: 640,
        accrualCapMinutes: 240,
        quantity: 0,
        pending: 0,
        cap: 0,
        canBuy: true,
        canCollect: false,
        collectUnavailableReason: 'notOwned',
      ));
      final unaffordable = await heightOf(kLockedProperty);
      expect(unaffordable, affordable,
          reason: 'an unowned card moved when its Buy refusal appeared or went');
    });
  });
}
