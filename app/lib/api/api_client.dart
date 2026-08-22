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

/// A server refusal that is part of normal play rather than a fault — not enough energy, level too
/// low, no skill points left.
///
/// Separate from [ApiException] because the UI must treat these differently: a refusal is a message
/// to show in place, whereas a transport error means the app has lost its footing and should offer a
/// retry.
class JobRefusedException implements Exception {
  const JobRefusedException(this.reason, this.detail);

  /// Stable token from the server: 'level', 'energy', 'noSkillPoints', or 'unknown'.
  final String reason;

  /// Human-readable detail from the server's problem response.
  final String detail;

  @override
  String toString() => 'JobRefusedException($reason): $detail';
}

abstract class ApiClient {
  Future<SessionResult> session(String? deviceToken);
  Future<Me> me(String deviceToken);
  Future<ContentPack> contentPack();

  /// The job catalog with this player's mastery and affordability folded in.
  Future<JobList> jobs(String deviceToken);

  /// Performs a job. Throws [JobRefusedException] when the server refuses it (409).
  Future<DoJobResult> doJob(String deviceToken, String jobId);

  /// Spends one skill point. Throws [JobRefusedException] with reason `noSkillPoints` when none remain.
  Future<Me> allocateSkill(String deviceToken, String target);

  /// The item catalog with this player's holdings, gating and effective stats folded in.
  ///
  /// One call serves both the Store and the Inventory: the server sends the whole catalog, not only
  /// owned rows, so the two screens cannot disagree about an item.
  Future<InventoryList> inventory(String deviceToken);

  /// Buys one copy of [itemId]. Throws [JobRefusedException] on a refusal ('level', 'cash').
  Future<BuyItemResult> buyItem(String deviceToken, String itemId);

  /// Wears [itemId], displacing whatever occupied its slot. Throws [JobRefusedException] on a
  /// refusal ('notOwned', 'level', 'alreadyEquipped').
  Future<EquipItemResult> equipItem(String deviceToken, String itemId);

  /// Takes [itemId] off, keeping the copies owned. Throws [JobRefusedException] with reason
  /// `notEquipped` when it was not being worn.
  Future<EquipItemResult> unequipItem(String deviceToken, String itemId);

  /// The property catalog with this player's holdings, takings and gating folded in.
  ///
  /// One call serves both halves of the Properties screen: the server sends the whole catalog, not
  /// only held rows. Reading it banks nothing — the pending figures are computed for the response
  /// and thrown away, so polling this route never costs the player a part-earned coin.
  Future<PropertyList> properties(String deviceToken);

  /// Buys one unit of [propertyId]. Throws [JobRefusedException] on a refusal ('level', 'cash').
  Future<BuyPropertyResult> buyProperty(String deviceToken, String propertyId);

  /// Banks everything [propertyId] has earned. Throws [JobRefusedException] on a refusal
  /// ('notOwned', 'nothingToCollect').
  Future<CollectPropertyResult> collectProperty(
      String deviceToken, String propertyId);

  /// Absolute URL for a pack art asset, by logical key ('logo', 'icon').
  ///
  /// Synchronous and unauthenticated: the server resolves the key against the manifest, so the
  /// client never needs to know which file backs it. This is what lets art be swapped in the
  /// pack without touching the app.
  String assetUrl(String key);
}

class HttpApiClient implements ApiClient {
  HttpApiClient({String? baseUrl, http.Client? client})
      : _baseUrl = baseUrl ?? _defaultBaseUrl(),
        _client = client ?? http.Client();

  final String _baseUrl;
  final http.Client _client;

  @override
  String assetUrl(String key) => '$_baseUrl/content/assets/$key';

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

  @override
  Future<JobList> jobs(String deviceToken) async {
    final response = await _client.get(
      Uri.parse('$_baseUrl/jobs'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _checkStatus(response);
    return JobList.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<DoJobResult> doJob(String deviceToken, String jobId) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl/jobs/$jobId/do'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _throwIfRefused(response);
    _checkStatus(response);
    return DoJobResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<Me> allocateSkill(String deviceToken, String target) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl/skills/allocate'),
      headers: {
        'Authorization': 'Bearer $deviceToken',
        'Content-Type': 'application/json',
      },
      body: jsonEncode({'target': target}),
    );
    _throwIfRefused(response);
    _checkStatus(response);
    final body = jsonDecode(response.body) as Map<String, dynamic>;
    return Me.fromJson(body['player'] as Map<String, dynamic>);
  }

  @override
  Future<InventoryList> inventory(String deviceToken) async {
    final response = await _client.get(
      Uri.parse('$_baseUrl/inventory'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _checkStatus(response);
    return InventoryList.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<BuyItemResult> buyItem(String deviceToken, String itemId) async {
    final response = await _post(deviceToken, '/items/$itemId/buy');
    return BuyItemResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<EquipItemResult> equipItem(String deviceToken, String itemId) async {
    final response = await _post(deviceToken, '/items/$itemId/equip');
    return EquipItemResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<EquipItemResult> unequipItem(String deviceToken, String itemId) async {
    final response = await _post(deviceToken, '/items/$itemId/unequip');
    return EquipItemResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<PropertyList> properties(String deviceToken) async {
    final response = await _client.get(
      Uri.parse('$_baseUrl/properties'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _checkStatus(response);
    return PropertyList.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<BuyPropertyResult> buyProperty(
      String deviceToken, String propertyId) async {
    final response = await _post(deviceToken, '/properties/$propertyId/buy');
    return BuyPropertyResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  @override
  Future<CollectPropertyResult> collectProperty(
      String deviceToken, String propertyId) async {
    final response = await _post(deviceToken, '/properties/$propertyId/collect');
    return CollectPropertyResult.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>);
  }

  /// POSTs a bodyless action and applies the two status checks every action shares.
  ///
  /// The item endpoints take no request body — the price and the stats are content, so there is
  /// nothing for a client to supply or tamper with.
  Future<http.Response> _post(String deviceToken, String path) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl$path'),
      headers: {'Authorization': 'Bearer $deviceToken'},
    );
    _throwIfRefused(response);
    _checkStatus(response);
    return response;
  }

  /// Turns a 409 problem response into a [JobRefusedException] carrying the server's reason token.
  ///
  /// The reason is read from the problem body rather than inferred from the status code, so the
  /// client never has to guess why an action was refused.
  void _throwIfRefused(http.Response response) {
    if (response.statusCode != 409) return;

    var reason = 'unknown';
    var detail = response.body;
    try {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      reason = body['reason'] as String? ?? 'unknown';
      detail = body['detail'] as String? ?? detail;
    } catch (_) {
      // Malformed problem body — fall back to the raw text rather than masking the refusal.
    }

    throw JobRefusedException(reason, detail);
  }
}
