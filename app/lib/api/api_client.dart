import 'dart:convert';
import 'dart:io' show Platform;

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:http/http.dart' as http;

import 'models.dart';

class ApiException implements Exception {
  const ApiException(this.statusCode, this.message);

  final int statusCode;
  final String message;

  @override
  String toString() => 'ApiException($statusCode): $message';
}

abstract class ApiClient {
  Future<SessionResult> session(String? deviceToken);
  Future<Me> me(String deviceToken);
  Future<ContentPack> contentPack();
}

class HttpApiClient implements ApiClient {
  HttpApiClient({String? baseUrl, http.Client? client})
      : _baseUrl = baseUrl ?? _defaultBaseUrl(),
        _client = client ?? http.Client();

  final String _baseUrl;
  final http.Client _client;

  static String _defaultBaseUrl() {
    const env = String.fromEnvironment('API_BASE_URL');
    if (env.isNotEmpty) return env;
    if (kIsWeb) return 'http://localhost:5080';
    if (!kIsWeb && _isAndroid()) return 'http://10.0.2.2:5080';
    return 'http://localhost:5080';
  }

  static bool _isAndroid() {
    try {
      return Platform.isAndroid;
    } catch (_) {
      return false;
    }
  }

  void _checkStatus(http.Response response) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(response.statusCode, response.body);
    }
  }

  @override
  Future<SessionResult> session(String? deviceToken) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl/session'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'deviceToken': deviceToken}),
    );
    _checkStatus(response);
    return SessionResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<Me> me(String deviceToken) async {
    final response = await _client.get(
      Uri.parse('$_baseUrl/me'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _checkStatus(response);
    return Me.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<ContentPack> contentPack() async {
    final response = await _client.get(
      Uri.parse('$_baseUrl/content/pack'),
    );
    _checkStatus(response);
    return ContentPack.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }
}
