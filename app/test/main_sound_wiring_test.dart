import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:underworld_empire/main.dart';

import 'fakes.dart';

void main() {
  final toggle = find.byKey(const Key('profile-sound-toggle'));

  Future<void> openProfile(WidgetTester tester) async {
    await tester.tap(find.text('Profile'));
    // Not pumpAndSettle: with no player loaded the Rap Sheet shows a spinner that never settles.
    await tester.pump(const Duration(milliseconds: 300));
    await tester.ensureVisible(toggle);
  }

  testWidgets('a mute survives Change server, and the toggle still reads off', (tester) async {
    SharedPreferences.setMockInitialValues({
      'server_address': 'http://localhost:5080',
      'device_token': 'token',
    });
    await tester.pumpWidget(UnderworldEmpireApp(
      apiClientFactory: (_) => FakeApiClient(throwOnSession: true),
    ));
    await tester.pumpAndSettle();

    await openProfile(tester);
    expect(tester.widget<SwitchListTile>(toggle).value, isTrue);
    await tester.tap(toggle);
    await tester.pump(const Duration(milliseconds: 300));
    expect(tester.widget<SwitchListTile>(toggle).value, isFalse);

    await tester.tap(find.text('Home'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('change-server')));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('server-address-field')),
      'http://localhost:5081',
    );
    await tester.tap(find.byKey(const Key('server-address-save')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('welcome-continue-as-guest')));
    await tester.pumpAndSettle();

    await openProfile(tester);
    expect(tester.widget<SwitchListTile>(toggle).value, isFalse);
  });
}
