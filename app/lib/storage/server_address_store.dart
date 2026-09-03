import 'package:shared_preferences/shared_preferences.dart';

/// Persists the server address the player chose on [ServerAddressScreen]. Mirrors [TokenStore]
/// exactly — same shape, same backing store — so the two behave identically to anything reading
/// this codebase's storage layer.
abstract class ServerAddressStore {
  Future<String?> read();
  Future<void> write(String address);
  Future<void> clear();
}

class SharedPrefsServerAddressStore implements ServerAddressStore {
  static const _key = 'server_address';

  @override
  Future<String?> read() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_key);
  }

  @override
  Future<void> write(String address) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key, address);
  }

  @override
  Future<void> clear() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_key);
  }
}
