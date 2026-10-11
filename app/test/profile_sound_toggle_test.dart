import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/profile_screen.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

Future<AppState> _pump(WidgetTester tester, {bool soundEnabled = true}) async {
  final state = AppState(
    apiClient: FakeApiClient(),
    tokenStore: InMemoryTokenStore(),
    soundEnabled: soundEnabled,
  );
  await state.bootstrap();
  await tester.pumpWidget(MaterialApp(home: ProfileScreen(appState: state)));
  await tester.pumpAndSettle();
  return state;
}

void main() {
  final toggle = find.byKey(const Key('profile-sound-toggle'));

  group('ProfileScreen — sound toggle', () {
    testWidgets('reflects the stored preference', (tester) async {
      await _pump(tester, soundEnabled: false);
      await tester.ensureVisible(toggle);

      expect(tester.widget<SwitchListTile>(toggle).value, isFalse);
    });

    testWidgets('turning it off mutes the game, and back on unmutes it', (tester) async {
      final state = await _pump(tester);
      await tester.ensureVisible(toggle);
      expect(tester.widget<SwitchListTile>(toggle).value, isTrue);

      await tester.tap(toggle);
      await tester.pumpAndSettle();
      expect(state.soundEnabled, isFalse);
      expect(tester.widget<SwitchListTile>(toggle).value, isFalse);

      await tester.tap(toggle);
      await tester.pumpAndSettle();
      expect(state.soundEnabled, isTrue);
    });
  });
}
