import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/screens/profile_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// Pumps ProfileScreen with a bootstrapped AppState, then settles enough frames for the
/// post-frame loadJobs() call to resolve.
Future<AppState> pumpProfile(WidgetTester tester, FakeApiClient api) async {
  final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
  await state.bootstrap();

  await tester.pumpWidget(MaterialApp(home: ProfileScreen(appState: state)));
  await tester.pumpAndSettle();

  return state;
}

void main() {
  group('ProfileScreen — stats', () {
    testWidgets('shows the four real stat values, including jobs done folded from mastery',
        (tester) async {
      await pumpProfile(tester, FakeApiClient());

      // kFakeMe: cash 12345, level 1, skillPoints 0. Default jobs
      // [kCheapJob, kUnaffordableJob, kLockedJob] each carry kFakeMastery (completions: 3), so
      // totalJobsCompleted folds to 9 once the post-frame loadJobs() resolves.
      expect(
        find.descendant(
          of: find.byKey(const Key('profile-stat-cash')),
          matching: find.text('12345'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byKey(const Key('profile-stat-level')),
          matching: find.text('1'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byKey(const Key('profile-stat-jobs-done')),
          matching: find.text('9'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byKey(const Key('profile-stat-skill-points')),
          matching: find.text('0'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('shows a loading indicator instead of the stat tiles when me is null',
        (tester) async {
      // A bootstrapped-but-not-yet-loaded AppState never reaches ProfileScreen in practice (it is
      // always pushed after Home has bootstrapped), but the defensive null check is asserted
      // directly rather than left untested.
      final state = AppState(apiClient: FakeApiClient(), tokenStore: InMemoryTokenStore());

      await tester.pumpWidget(MaterialApp(home: ProfileScreen(appState: state)));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.byKey(const Key('profile-stat-cash')), findsNothing);
    });
  });

  group('ProfileScreen — portrait', () {
    testWidgets('falls back to the generic person icon when the pack declares no icon asset',
        (tester) async {
      // The default FakeApiClient content pack ships no assets at all.
      await pumpProfile(tester, FakeApiClient());

      expect(find.byIcon(Icons.person_outline), findsOneWidget);
      expect(find.byKey(const Key('profile-portrait-image')), findsNothing);
    });

    testWidgets('requests the icon from the manifest-driven asset route when the pack declares one',
        (tester) async {
      const pack = ContentPack(
        name: 'Underworld Empire',
        cashLabel: 'Cash',
        premiumLabel: 'Gold Bars',
        primaryColorHex: '#B71C1C',
        assets: {'icon': 'assets/icon.png'},
      );
      await pumpProfile(tester, FakeApiClient(contentPackResult: pack));

      final image =
          tester.widget<Image>(find.byKey(const Key('profile-portrait-image')));
      expect((image.image as NetworkImage).url, endsWith('/content/assets/icon'));
    });
  });

  group('ProfileScreen — coming soon panel', () {
    testWidgets('renders and reads as not-yet-live rather than as a real feature',
        (tester) async {
      await pumpProfile(tester, FakeApiClient());

      expect(find.byKey(const Key('profile-coming-soon')), findsOneWidget);

      final text = tester.widget<Text>(
        find.descendant(
          of: find.byKey(const Key('profile-coming-soon')),
          matching: find.byType(Text),
        ),
      );
      final message = text.data ?? '';

      // It's fine for the copy to name the missing features; what it must not do is phrase them
      // as if they already exist (a bare "Rank" label or a "Respect" progress readout).
      expect(message, contains('coming'));
      expect(message, isNot(contains('Respect:')));
      expect(message, isNot(contains('Rank:')));
    });
  });

  group('ProfileScreen — navigation', () {
    testWidgets('tapping Skills pushes SkillsScreen', (tester) async {
      await pumpProfile(tester, FakeApiClient());

      await tester.tap(find.byKey(const Key('profile-go-to-skills')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('skill-points')), findsOneWidget);
    });
  });
}
