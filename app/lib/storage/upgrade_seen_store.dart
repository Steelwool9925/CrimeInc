import 'package:shared_preferences/shared_preferences.dart';

/// Remembers which advised upgrade a player has already been shown, so the Market tile's dot stays
/// cleared across a reload instead of coming back for the same item.
abstract class UpgradeSeenStore {
  Future<String?> read(String playerId);
  Future<void> write(String playerId, String itemId);
}

class SharedPrefsUpgradeSeenStore implements UpgradeSeenStore {
  static String _key(String playerId) => 'upgrade_seen_$playerId';

  @override
  Future<String?> read(String playerId) async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_key(playerId));
  }

  @override
  Future<void> write(String playerId, String itemId) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key(playerId), itemId);
  }
}

/// Forgets everything when the app does. The default for an [AppState] given no store of its own.
class InMemoryUpgradeSeenStore implements UpgradeSeenStore {
  final Map<String, String> _seen = {};

  @override
  Future<String?> read(String playerId) async => _seen[playerId];

  @override
  Future<void> write(String playerId, String itemId) async => _seen[playerId] = itemId;
}
