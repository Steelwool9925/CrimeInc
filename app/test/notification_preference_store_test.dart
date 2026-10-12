import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:underworld_empire/storage/notification_preference_store.dart';

/// The real store, not the in-memory fake every other test uses.
///
/// "Ask exactly once, ever" survives an app restart only because this class reads and writes the
/// same key, and because it keeps three states apart: never asked, said yes, said no. A typo in the
/// key or a null collapsed into false would pass every other test in the suite.
void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('a player who has never been asked reads null', () async {
    final store = SharedPrefsNotificationPreferenceStore();

    expect(await store.read(), isNull);
  });

  test('a granted choice survives a restart', () async {
    await SharedPrefsNotificationPreferenceStore().write(true);

    // A new instance stands in for the next launch: nothing is held in memory.
    expect(await SharedPrefsNotificationPreferenceStore().read(), isTrue);
  });

  test('a refusal survives a restart and is not read back as never-asked', () async {
    await SharedPrefsNotificationPreferenceStore().write(false);

    final reread = await SharedPrefsNotificationPreferenceStore().read();
    expect(reread, isFalse);
    expect(reread, isNotNull, reason: 'false must stay distinguishable from never having asked');
  });

  test('the last write wins', () async {
    final store = SharedPrefsNotificationPreferenceStore();

    await store.write(true);
    await store.write(false);

    expect(await SharedPrefsNotificationPreferenceStore().read(), isFalse);
  });

  test('writes land under the documented key', () async {
    await SharedPrefsNotificationPreferenceStore().write(true);

    final prefs = await SharedPreferences.getInstance();
    expect(prefs.getBool('return_notifications_enabled'), isTrue,
        reason: 'the key is persisted state; renaming it silently re-asks every existing player');
  });
}
