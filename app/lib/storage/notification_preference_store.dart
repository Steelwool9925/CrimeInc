import 'package:shared_preferences/shared_preferences.dart';

/// Persists whether the player wants return notifications.
///
/// Three states, not two: null means the player has never been asked. That is what lets the game
/// ask exactly once, after the first job, instead of at launch or on every job. Mirrors
/// [ServerAddressStore] and [TokenStore] — same shape, same backing store.
abstract class NotificationPreferenceStore {
  Future<bool?> read();
  Future<void> write(bool enabled);
}

class SharedPrefsNotificationPreferenceStore implements NotificationPreferenceStore {
  static const _key = 'return_notifications_enabled';

  @override
  Future<bool?> read() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(_key);
  }

  @override
  Future<void> write(bool enabled) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_key, enabled);
  }
}

/// Remembers the choice for the life of the process. [AppState]'s default, so nothing touches
/// storage unless the app wires the real one.
class InMemoryNotificationPreferenceStore implements NotificationPreferenceStore {
  InMemoryNotificationPreferenceStore([this._value]);

  bool? _value;

  @override
  Future<bool?> read() async => _value;

  @override
  Future<void> write(bool enabled) async => _value = enabled;
}
