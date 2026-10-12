import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:underworld_empire/storage/upgrade_seen_store.dart';

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  group('SharedPrefsUpgradeSeenStore', () {
    test('has nothing for a player who has seen nothing', () async {
      expect(await SharedPrefsUpgradeSeenStore().read('player-1'), isNull);
    });

    test('remembers what was written, across instances', () async {
      await SharedPrefsUpgradeSeenStore().write('player-1', 'brass-knuckles');

      expect(await SharedPrefsUpgradeSeenStore().read('player-1'), 'brass-knuckles');
    });

    test('keeps one answer per player', () async {
      final store = SharedPrefsUpgradeSeenStore();
      await store.write('player-1', 'brass-knuckles');
      await store.write('player-2', 'leather-jacket');

      expect(await store.read('player-1'), 'brass-knuckles');
      expect(await store.read('player-2'), 'leather-jacket');
    });

    test('a later item replaces the earlier one', () async {
      final store = SharedPrefsUpgradeSeenStore();
      await store.write('player-1', 'brass-knuckles');
      await store.write('player-1', 'switchblade');

      expect(await store.read('player-1'), 'switchblade');
    });
  });

  group('InMemoryUpgradeSeenStore', () {
    test('remembers within one instance and forgets with it', () async {
      final store = InMemoryUpgradeSeenStore();
      await store.write('player-1', 'brass-knuckles');

      expect(await store.read('player-1'), 'brass-knuckles');
      expect(await InMemoryUpgradeSeenStore().read('player-1'), isNull);
    });
  });
}
