import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:underworld_empire/api/api_client.dart';
import 'package:underworld_empire/state/app_state.dart';

import 'fakes.dart';

/// What actually goes over the wire when a job is attempted.
///
/// The fake client in `fakes.dart` stands in for the whole HTTP layer, so a screen test can show that
/// the extras reach `ApiClient.doJob` and still say nothing about whether the real client puts them in
/// the request. These tests run the real [HttpApiClient] against a recording [MockClient]. The reply
/// is a refusal on purpose: only the request is under test, and a refusal is the shortest valid reply.
void main() {
  late http.Request seen;

  HttpApiClient clientRecording() => HttpApiClient(
        baseUrl: 'http://api.test',
        client: MockClient((request) async {
          seen = request;
          return http.Response(jsonEncode({'reason': 'items', 'detail': 'refused'}), 409,
              headers: {'content-type': 'application/json'});
        }),
      );

  Future<void> attempt(HttpApiClient api, {Map<String, int> extras = const {}}) async {
    try {
      await api.doJob('token-123', 'hijack-a-delivery-truck', extras: extras);
    } on JobRefusedException {
      // Expected: the reply is a refusal.
    }
  }

  test('with extras, the request is a JSON body naming them', () async {
    await attempt(clientRecording(), extras: {'switchblade': 2});

    expect(seen.method, 'POST');
    expect(seen.url.path, '/jobs/hijack-a-delivery-truck/do');
    expect(seen.headers['Content-Type'], startsWith('application/json'));
    expect(jsonDecode(seen.body), {
      'extras': {'switchblade': 2},
    });
  });

  test('with none, there is no body at all, as before', () async {
    await attempt(clientRecording());

    expect(seen.body, isEmpty);
    expect(seen.headers['Content-Type'], isNull);
  });

  test('the bearer token is sent either way', () async {
    await attempt(clientRecording(), extras: {'switchblade': 1});
    expect(seen.headers['Authorization'], 'Bearer token-123');

    await attempt(clientRecording());
    expect(seen.headers['Authorization'], 'Bearer token-123');
  });

  test('state hands the chosen extras to the client', () async {
    final api = FakeApiClient();
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    await state.bootstrap();
    await state.loadJobs();

    await state.doJob('hijack-a-delivery-truck', extras: {'switchblade': 2});

    expect(api.doJobExtras, [
      {'switchblade': 2},
    ]);
  });

  test('state sends none when none were chosen', () async {
    final api = FakeApiClient();
    final state = AppState(apiClient: api, tokenStore: InMemoryTokenStore());
    await state.bootstrap();
    await state.loadJobs();

    await state.doJob('hijack-a-delivery-truck');

    expect(api.doJobExtras, [<String, int>{}]);
  });
}
