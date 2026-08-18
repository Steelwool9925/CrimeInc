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

  // ---- Jobs ----
  List<Job> _jobs = const [];
  bool _jobsLoading = false;
  String? _jobsError;

  /// The id of the job currently being performed, so the UI can disable just that row.
  String? _jobInFlight;

  /// The most recent job outcome, shown in the result panel until dismissed.
  JobResult? _lastJobResult;

  /// A pending level-up waiting to be shown as a dialog, cleared once acknowledged.
  LevelUp? _pendingLevelUp;

  /// A refusal or error from the last action, shown transiently.
  String? _actionMessage;

  LoadStatus get status => _status;
  Me? get me => _me;
  ContentPack? get contentPack => _contentPack;
  String? get errorMessage => _errorMessage;

  List<Job> get jobs => _jobs;
  bool get jobsLoading => _jobsLoading;
  String? get jobsError => _jobsError;
  String? get jobInFlight => _jobInFlight;
  JobResult? get lastJobResult => _lastJobResult;
  LevelUp? get pendingLevelUp => _pendingLevelUp;
  String? get actionMessage => _actionMessage;

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

  // -------------------------------------------------------------------------
  // Jobs
  // -------------------------------------------------------------------------

  /// Loads the job catalog. Safe to call repeatedly; concurrent calls are collapsed.
  Future<void> loadJobs() async {
    final token = _deviceToken;
    if (token == null || _jobsLoading) return;

    _jobsLoading = true;
    _jobsError = null;
    notifyListeners();

    try {
      final list = await _apiClient.jobs(token);
      _jobs = list.jobs;
    } catch (e) {
      _jobsError = e.toString();
    } finally {
      _jobsLoading = false;
      notifyListeners();
    }
  }

  /// Performs [jobId].
  ///
  /// The server returns the full post-action player state, so this does not follow up with a /me
  /// call — that is the round trip the do-job response exists to remove. A refusal (not enough
  /// energy, level too low) is surfaced through [actionMessage] rather than as an error state,
  /// because it is normal play, not a failure.
  Future<void> doJob(String jobId) async {
    final token = _deviceToken;
    if (token == null || _jobInFlight != null) return;

    _jobInFlight = jobId;
    _actionMessage = null;
    notifyListeners();

    try {
      final outcome = await _apiClient.doJob(token, jobId);

      _lastJobResult = outcome.result;
      _me = outcome.player;
      if (outcome.levelUp != null) {
        _pendingLevelUp = outcome.levelUp;
      }

      // The catalog carries per-job mastery and affordability, both of which just changed.
      _applyOutcomeToCatalog(outcome);
    } on JobRefusedException catch (e) {
      _actionMessage = switch (e.reason) {
        'energy' => 'Not enough energy.',
        'level' => 'Your level is too low for that job.',
        _ => e.detail,
      };
    } catch (e) {
      _actionMessage = 'Could not do that job. $e';
    } finally {
      _jobInFlight = null;
      notifyListeners();
    }
  }

  /// Folds a job outcome into the cached catalog so the list reflects the new mastery and energy
  /// without a second network call.
  void _applyOutcomeToCatalog(DoJobResult outcome) {
    final energy = _me?.energy.current;
    final level = _me?.level;

    _jobs = _jobs.map((job) {
      final mastery =
          job.id == outcome.result.jobId ? outcome.result.mastery : job.mastery;

      final meetsLevel = level == null || level >= job.levelRequirement;
      final affords = energy == null || energy >= job.energyCost;
      final reason = !meetsLevel ? 'level' : (!affords ? 'energy' : null);

      return Job(
        id: job.id,
        name: job.name,
        description: job.description,
        area: job.area,
        energyCost: job.energyCost,
        levelRequirement: job.levelRequirement,
        cashMin: job.cashMin,
        cashMax: job.cashMax,
        xp: job.xp,
        mastery: mastery,
        canAttempt: reason == null,
        unavailableReason: reason,
      );
    }).toList(growable: false);
  }

  /// Clears the result panel.
  void dismissJobResult() {
    _lastJobResult = null;
    notifyListeners();
  }

  /// Acknowledges the level-up dialog.
  void acknowledgeLevelUp() {
    _pendingLevelUp = null;
    notifyListeners();
  }

  /// Clears a transient action message.
  void clearActionMessage() {
    _actionMessage = null;
    notifyListeners();
  }

  // -------------------------------------------------------------------------
  // Skills
  // -------------------------------------------------------------------------

  /// Spends one skill point on [target] ('attack' or 'defense').
  Future<void> allocateSkill(String target) async {
    final token = _deviceToken;
    if (token == null) return;

    _actionMessage = null;
    notifyListeners();

    try {
      _me = await _apiClient.allocateSkill(token, target);
    } on JobRefusedException catch (e) {
      _actionMessage = e.reason == 'noSkillPoints'
          ? 'No skill points to spend.'
          : e.detail;
    } catch (e) {
      _actionMessage = 'Could not allocate that point. $e';
    }

    notifyListeners();
  }

  @override
  void dispose() {
    stopPolling();
    super.dispose();
  }
}
