import 'dart:async';

import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../storage/token_store.dart';

enum LoadStatus { loading, ready, error }

class AppState extends ChangeNotifier {
  AppState({
    required ApiClient apiClient,
    required TokenStore tokenStore,
  })  : _apiClient = apiClient,
        _tokenStore = tokenStore;

  final ApiClient _apiClient;
  final TokenStore _tokenStore;

  LoadStatus _status = LoadStatus.loading;
  Me? _me;
  ContentPack? _contentPack;
  String? _errorMessage;
  Timer? _pollingTimer;
  String? _deviceToken;

  LoadStatus get status => _status;
  Me? get me => _me;
  ContentPack? get contentPack => _contentPack;
  String? get errorMessage => _errorMessage;

  Future<void> bootstrap() async {
    _status = LoadStatus.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      final storedToken = await _tokenStore.read();
      final result = await _apiClient.session(storedToken);
      _deviceToken = result.deviceToken;
      await _tokenStore.write(_deviceToken!);

      try {
        _contentPack = await _apiClient.contentPack();
      } catch (_) {
        // best-effort; keep null / defaults
      }

      _me = await _apiClient.me(_deviceToken!);
      _status = LoadStatus.ready;
    } catch (e) {
      _status = LoadStatus.error;
      _errorMessage = e.toString();
    }

    notifyListeners();
  }

  Future<void> refresh() async {
    final token = _deviceToken;
    if (token == null) return;
    try {
      _me = await _apiClient.me(token);
      if (_status != LoadStatus.ready) {
        _status = LoadStatus.ready;
      }
    } catch (e) {
      // silent on poll failure — keep last known state
    }
    notifyListeners();
  }

  void startPolling([Duration interval = const Duration(seconds: 10)]) {
    _pollingTimer?.cancel();
    _pollingTimer = Timer.periodic(interval, (_) => refresh());
  }

  void stopPolling() {
    _pollingTimer?.cancel();
    _pollingTimer = null;
  }

  @override
  void dispose() {
    stopPolling();
    super.dispose();
  }
}
