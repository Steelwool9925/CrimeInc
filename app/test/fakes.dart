import 'package:underworld_empire/api/api_client.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/storage/token_store.dart';

/// A canned mastery standing: 3 of 10 completions toward rank 1 of 5.
const kFakeMastery = JobMastery(
  completions: 3,
  rank: 0,
  maxRank: 5,
  completionsUntilNextRank: 7,
);

/// An attemptable level-1 job.
const kCheapJob = Job(
  id: 'shake-down-corner-store',
  name: 'Shake Down a Corner Store',
  description: 'Lean on the owner for a cut of the register.',
  area: 'downtown',
  energyCost: 1,
  levelRequirement: 1,
  cashMin: 15,
  cashMax: 30,
  xp: 10,
  mastery: kFakeMastery,
  canAttempt: true,
);

/// A job locked behind a level requirement.
const kLockedJob = Job(
  id: 'rob-an-armored-transport',
  name: 'Rob an Armored Transport',
  description: 'Three cars, one blocked intersection.',
  area: 'uptown',
  energyCost: 9,
  levelRequirement: 8,
  cashMin: 700,
  cashMax: 1300,
  xp: 180,
  mastery: kFakeMastery,
  canAttempt: false,
  unavailableReason: 'level',
);

/// A job the player cannot currently afford.
const kUnaffordableJob = Job(
  id: 'hijack-a-delivery-truck',
  name: 'Hijack a Delivery Truck',
  description: 'Wait at the light.',
  area: 'docks',
  energyCost: 4,
  levelRequirement: 1,
  cashMin: 140,
  cashMax: 260,
  xp: 55,
  mastery: kFakeMastery,
  canAttempt: false,
  unavailableReason: 'energy',
);

const kFakeMe = Me(
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
);

class FakeApiClient implements ApiClient {
  FakeApiClient({
    SessionResult? sessionResult,
    Me? meResult,
    ContentPack? contentPackResult,
    JobList? jobsResult,
    DoJobResult? doJobResult,
    Me? allocateResult,
    bool throwOnSession = false,
    bool throwOnMe = false,
    bool throwOnContentPack = false,
    bool throwOnJobs = false,
    this.refuseDoJobWith,
    this.refuseAllocateWith,
  })  : _sessionResult = sessionResult ??
            const SessionResult(
              deviceToken: 'fake-token-abc',
              playerId: 'player-guid-123',
            ),
        _meResult = meResult ?? kFakeMe,
        _contentPackResult = contentPackResult ??
            const ContentPack(
              name: 'Default',
              cashLabel: 'Cash',
              premiumLabel: 'Gems',
              primaryColorHex: '#6200EE',
            ),
        _jobsResult = jobsResult ??
            const JobList(
              jobs: [kCheapJob, kUnaffordableJob, kLockedJob],
              currentEnergy: 7,
              level: 1,
            ),
        _doJobResult = doJobResult ??
            const DoJobResult(
              result: JobResult(
                jobId: 'shake-down-corner-store',
                energySpent: 1,
                cashEarned: 22,
                xpEarned: 10,
                mastery: JobMastery(
                  completions: 4,
                  rank: 0,
                  maxRank: 5,
                  completionsUntilNextRank: 6,
                ),
                masteryRankedUp: false,
              ),
              player: kFakeMe,
            ),
        _allocateResult = allocateResult,
        _throwOnSession = throwOnSession,
        _throwOnMe = throwOnMe,
        _throwOnContentPack = throwOnContentPack,
        _throwOnJobs = throwOnJobs;

  final SessionResult _sessionResult;
  final Me _meResult;
  final ContentPack _contentPackResult;
  final JobList _jobsResult;
  final DoJobResult _doJobResult;
  final Me? _allocateResult;
  final bool _throwOnSession;
  final bool _throwOnMe;
  final bool _throwOnContentPack;
  final bool _throwOnJobs;

  /// When set, doJob throws a [JobRefusedException] with this reason.
  final String? refuseDoJobWith;

  /// When set, allocateSkill throws a [JobRefusedException] with this reason.
  final String? refuseAllocateWith;

  final List<String?> sessionCalls = [];
  int meCalls = 0;
  int contentPackCalls = 0;
  int jobsCalls = 0;
  final List<String> doJobCalls = [];
  final List<String> allocateCalls = [];

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

  @override
  Future<JobList> jobs(String deviceToken) async {
    jobsCalls++;
    if (_throwOnJobs) throw const ApiException(500, 'jobs error');
    return _jobsResult;
  }

  @override
  Future<DoJobResult> doJob(String deviceToken, String jobId) async {
    doJobCalls.add(jobId);
    if (refuseDoJobWith != null) {
      throw JobRefusedException(refuseDoJobWith!, 'refused: $refuseDoJobWith');
    }
    return _doJobResult;
  }

  @override
  Future<Me> allocateSkill(String deviceToken, String target) async {
    allocateCalls.add(target);
    if (refuseAllocateWith != null) {
      throw JobRefusedException(refuseAllocateWith!, 'refused: $refuseAllocateWith');
    }
    return _allocateResult ?? _meResult;
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
