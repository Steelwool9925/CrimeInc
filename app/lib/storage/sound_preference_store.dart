import 'package:shared_preferences/shared_preferences.dart';

/// Remembers whether the player turned the game's sound off. Per device, not per player: it is a
/// property of the phone in their hand, so it survives logging out. Mirrors [TokenStore].
abstract class SoundPreferenceStore {
  /// Null when the player has never chosen, which means sound is on.
  Future<bool?> read();
  Future<void> write(bool enabled);
}

class SharedPrefsSoundPreferenceStore implements SoundPreferenceStore {
  static const _key = 'sound_enabled';

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

/// Forgets everything when the app does. The default for an [AppState] given no store of its own.
class InMemorySoundPreferenceStore implements SoundPreferenceStore {
  bool? _enabled;

  @override
  Future<bool?> read() async => _enabled;

  @override
  Future<void> write(bool enabled) async => _enabled = enabled;
}
