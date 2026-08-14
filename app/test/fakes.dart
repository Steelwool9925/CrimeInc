import 'package:underworld_empire/api/api_client.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/storage/token_store.dart';

class FakeApiClient implements ApiClient {
  FakeApiClient({
    SessionResult? sessionResult,
    Me? meResult,
    ContentPack? contentPackResult,
    bool throwOnSession = false,
    bool throwOnMe = false,
    bool throwOnContentPack = false,
  })  : _sessionResult = sessionResult ??
            const SessionResult(
              deviceToken: 'fake-token-abc',
              playerId: 'player-guid-123',
            ),
        _meResult = meResult ??
            const Me(
              playerId: 'player-guid-123',
              energy: MeterState(
                current: 7,
                max: 10,
                regenSecondsPerPoint: 300,
                secondsUntilNextPoint: 42,
              ),
              stamina: MeterState(
                current: 5,
                max: 10,
                regenSecondsPerPoint: 300,
                secondsUntilNextPoint: 120,
              ),
              health: MeterState(
                current: 10,
                max: 10,
                regenSecondsPerPoint: 300,
              ),
              cash: 12345,
              premiumCurrency: 0,
              level: 1,
              xp: 0,
              skillPoints: 0,
              attack: 10,
              defense: 10,
            ),
        _contentPackResult = contentPackResult ??
            const ContentPack(
              name: 'Default',
              cashLabel: 'Cash',
              premiumLabel: 'Gems',
              primaryColorHex: '#6200EE',
            ),
        _throwOnSession = throwOnSession,
        _throwOnMe = throwOnMe,
        _throwOnContentPack = throwOnContentPack;

  final SessionResult _sessionResult;
  final Me _meResult;
  final ContentPack _contentPackResult;
  final bool _throwOnSession;
  final bool _throwOnMe;
  final bool _throwOnContentPack;

  final List<String?> sessionCalls = [];
  int meCalls = 0;
  int contentPackCalls = 0;

  @override
  Future<SessionResult> session(String? deviceToken) async {
    sessionCalls.add(deviceToken);
    if (_throwOnSession) throw const ApiException(500, 'session error');
    return _sessionResult;
  }

  @override
  Future<Me> me(String deviceToken) async {
    meCalls++;
    if (_throwOnMe) throw const ApiException(500, 'me error');
    return _meResult;
  }

  @override
  Future<ContentPack> contentPack() async {
    contentPackCalls++;
    if (_throwOnContentPack) throw const ApiException(500, 'pack error');
    return _contentPackResult;
  }
}

class InMemoryTokenStore implements TokenStore {
  String? _token;

  @override
  Future<String?> read() async => _token;

  @override
  Future<void> write(String token) async => _token = token;

  @override
  Future<void> clear() async => _token = null;
}
