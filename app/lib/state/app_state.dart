import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../audio/outcome_sfx.dart';
import '../notifications/return_notifier.dart';
import '../notifications/return_schedule.dart';
import '../storage/notification_preference_store.dart';
import '../storage/sound_preference_store.dart';
import '../storage/token_store.dart';
import '../storage/upgrade_seen_store.dart';
import '../widgets/job_drama.dart';
import '../widgets/upgrade_advice.dart';

enum LoadStatus { loading, ready, error }

/// The part of the game an [AppState.actionMessage] came from. A refusal is about one thing the
/// player just did, so only the screens for that thing show it: an error from spending a skill point
/// has no business on the Market.
enum ActionScope { jobs, items, properties, skills }

class AppState extends ChangeNotifier {
  AppState({
    required ApiClient apiClient,
    required TokenStore tokenStore,
    UpgradeSeenStore? seenStore,
    this.revealDelay = Duration.zero,
    OutcomeSfx? sfx,
    SoundPreferenceStore? soundStore,
    bool soundEnabled = true,
    ReturnNotifier returnNotifier = const SilentReturnNotifier(),
    NotificationPreferenceStore? notificationPreferenceStore,
    DateTime Function() clock = DateTime.now,
  })  : _apiClient = apiClient,
        _tokenStore = tokenStore,
        _seenStore = seenStore ?? InMemoryUpgradeSeenStore(),
        _sfx = sfx ?? const SilentOutcomeSfx(),
        _soundStore = soundStore ?? InMemorySoundPreferenceStore(),
        _soundEnabled = soundEnabled,
        _returnNotifier = returnNotifier,
        _notificationPreferenceStore =
            notificationPreferenceStore ?? InMemoryNotificationPreferenceStore(),
        _clock = clock;

  /// How long the app holds a job's outcome, already in hand, before showing it. `main.dart` passes
  /// this; it defaults to zero so a bare [AppState] (every test) never waits.
  static const Duration defaultRevealDelay = Duration(milliseconds: 400);

  /// See [defaultRevealDelay]. Zero skips the hold entirely rather than waiting a frame.
  final Duration revealDelay;

  final ApiClient _apiClient;
  final TokenStore _tokenStore;
  final UpgradeSeenStore _seenStore;
  final OutcomeSfx _sfx;
  final SoundPreferenceStore _soundStore;
  final ReturnNotifier _returnNotifier;
  final NotificationPreferenceStore _notificationPreferenceStore;
  final DateTime Function() _clock;

  bool _soundEnabled;

  /// Whether job cues play. Starts from the stored preference, which `main.dart` reads before
  /// building this, so the very first job already honours a mute.
  bool get soundEnabled => _soundEnabled;

  bool _disposed = false;

  /// Work started without being awaited (the bootstrap catalog read) can finish after the owner has
  /// let go of this object; telling listeners then would throw, and there is nobody left to tell.
  @override
  void notifyListeners() {
    if (_disposed) return;
    super.notifyListeners();
  }

  LoadStatus _status = LoadStatus.loading;

  // Every assignment to [_me] stamps when it arrived: the energy wait it carries runs from that
  // moment, and the return notifications are scheduled from it.
  Me? _meValue;
  DateTime? _meFetchedAt;
  Me? get _me => _meValue;
  set _me(Me? value) {
    _meValue = value;
    _meFetchedAt = _clock();
  }
  ContentPack? _contentPack;
  String? _errorMessage;
  Timer? _pollingTimer;
  String? _deviceToken;

  // ---- Jobs ----
  List<Job> _jobs = const [];
  double _levelPayoutBonus = 0;
  bool _jobsLoading = false;
  int _jobsRequest = 0;
  String? _jobsError;

  /// The id of the job currently being performed, so the UI can disable just that row.
  String? _jobInFlight;

  /// The most recent job outcome, shown in the result panel until dismissed.
  JobResult? _lastJobResult;

  /// How big a win [_lastJobResult] was. Worked out once, when the result lands, and kept with it: a
  /// level-up being acknowledged must not shrink the reveal that is still playing.
  JobDrama? _lastJobDrama;

  /// Whether a received job outcome is being held back for a beat before it is shown.
  bool _anticipating = false;
  Completer<void>? _revealHold;
  Timer? _revealTimer;

  /// A pending level-up waiting to be shown as a dialog, cleared once acknowledged.
  LevelUp? _pendingLevelUp;

  /// A refusal or error from the last action, shown transiently.
  String? _actionMessage;
  ActionScope? _actionMessageScope;

  // ---- Inventory & store ----
  List<InventoryItem> _inventory = const [];
  LoadoutStats? _loadout;
  bool _inventoryLoading = false;
  int _inventoryRequest = 0;
  String? _inventoryError;

  /// The player's level when the catalog on hand was requested. The gating on each row includes a
  /// `level` verdict, so a level-up makes that picture stale in a way cash changes do not.
  int? _inventoryLevel;

  /// The level the newest catalog request was made at. While a read is in flight this, not
  /// [_inventoryLevel], says what the answer will reflect.
  int? _inventoryRequestedLevel;

  // The upgrade advice, derived from the catalog and cash and recomputed only when one of them
  // changes — never on a timer. See [upgradeAdvice].
  UpgradeAdvice? _advice;
  List<InventoryItem>? _adviceInventory;
  int? _adviceCash;

  /// The id of the item currently being bought or worn, so the UI can disable just that row.
  String? _itemInFlight;

  /// True while a skill point is being spent, so the screen can disable both buttons.
  bool _skillInFlight = false;

  // ---- Properties ----
  List<Property> _properties = const [];
  DateTime? _propertiesFetchedAt;
  bool _propertiesLoading = false;
  String? _propertiesError;

  /// The id of the property currently being bought or collected from, so the UI can disable just
  /// that row.
  String? _propertyInFlight;

  LoadStatus get status => _status;
  Me? get me => _me;
  ContentPack? get contentPack => _contentPack;
  String? get errorMessage => _errorMessage;

  /// URL for a pack art asset by logical key — see [ApiClient.assetUrl].
  String assetUrl(String key) => _apiClient.assetUrl(key);

  List<Job> get jobs => _jobs;

  /// The extra share of cash and XP the player's level adds to every job, as of the last jobs read.
  double get levelPayoutBonus => _levelPayoutBonus;

  /// Total completions across every job, for the Rap Sheet's "Jobs Done" stat. A pure fold over
  /// [jobs] — no new endpoint: `GET /jobs` already returns each job's `mastery.completions`.
  int get totalJobsCompleted =>
      _jobs.fold(0, (sum, job) => sum + job.mastery.completions);

  bool get skillInFlight => _skillInFlight;

  bool get jobsLoading => _jobsLoading;
  String? get jobsError => _jobsError;
  String? get jobInFlight => _jobInFlight;
  JobResult? get lastJobResult => _lastJobResult;
  JobDrama? get lastJobDrama => _lastJobDrama;

  /// True while the outcome of a job has arrived but is deliberately not yet shown. The job is still
  /// in flight for this whole stretch, so nothing else can be started under it.
  bool get anticipating => _anticipating;
  LevelUp? get pendingLevelUp => _pendingLevelUp;
  String? get actionMessage => _actionMessage;

  /// The message, but only to the screens it belongs to; null for everyone else.
  String? actionMessageFor(ActionScope scope) => _actionMessageScope == scope ? _actionMessage : null;

  void _setActionMessage(ActionScope scope, String text) {
    _actionMessage = text;
    _actionMessageScope = scope;
  }

  List<InventoryItem> get inventory => _inventory;

  /// Effective combat stats. Null until the inventory has been read — [Me] carries only the
  /// player's base stats, because building a loadout costs a query the progression endpoints do not
  /// need to pay for. Bootstrap starts that read without waiting for it, so Home briefly has no
  /// loadout on a cold start.
  LoadoutStats? get loadout => _loadout;

  bool get inventoryLoading => _inventoryLoading;

  /// The advised item the player has already been shown in the Market. The accent dot is for
  /// attention, so it clears once they have looked; a different item being advised later raises it
  /// again, because that is news.
  String? _upgradeSeenItemId;

  /// Whether the saved answer has been read (or given up on). Until then the dot stays hidden rather
  /// than flash for an item the player has in fact already been shown.
  bool _upgradeSeenLoaded = false;

  /// Whether the Market tile's dot should show: there is advice and the player has not yet opened
  /// the Market since it named this item. The words ("Upgrade available") do not depend on this.
  bool get upgradeBadgeVisible {
    final advice = upgradeAdvice;
    return _upgradeSeenLoaded && advice != null && advice.item.id != _upgradeSeenItemId;
  }

  /// Records that the Market was opened while [upgradeAdvice] named an item. A no-op, with no
  /// notification, once recorded — safe to call on every build of the Market.
  void markUpgradeSeen() {
    final id = upgradeAdvice?.item.id;
    if (id == null || id == _upgradeSeenItemId) return;
    _upgradeSeenItemId = id;
    notifyListeners();
    unawaited(_rememberSeen(id));
  }

  /// Best effort in both directions: not being able to remember costs only the dot coming back.
  Future<void> _rememberSeen(String itemId) async {
    final playerId = _me?.playerId;
    if (playerId == null) return;
    try {
      await _seenStore.write(playerId, itemId);
    } catch (_) {}
  }

  /// The one upgrade worth mentioning right now, or null. Derived from data already held — the
  /// catalog and cash — so showing it costs no request, and memoised so a rebuild is a comparison.
  UpgradeAdvice? get upgradeAdvice {
    final me = _me;
    if (me == null) return null;
    if (!identical(_adviceInventory, _inventory) || _adviceCash != me.cash) {
      _advice = adviseUpgrade(me, _inventory);
      _adviceInventory = _inventory;
      _adviceCash = me.cash;
    }
    return _advice;
  }

  String? get inventoryError => _inventoryError;
  String? get itemInFlight => _itemInFlight;

  /// The rows the player actually holds — what the Inventory screen lists.
  ///
  /// Derived rather than fetched: GET /inventory sends the whole catalog so the Store and the
  /// Inventory cannot disagree about an item, and this is the Inventory's view of that one list.
  List<InventoryItem> get ownedItems =>
      _inventory.where((i) => i.isOwned).toList(growable: false);

  List<Property> get properties => _properties;
  bool get propertiesLoading => _propertiesLoading;
  String? get propertiesError => _propertiesError;
  String? get propertyInFlight => _propertyInFlight;

  /// The properties the player actually holds — the collecting half of the screen.
  ///
  /// Derived rather than fetched, for the same reason [ownedItems] is: GET /properties sends the
  /// whole catalog so the two halves cannot disagree, and this is one view of that single list.
  List<Property> get ownedProperties =>
      _properties.where((p) => p.isOwned).toList(growable: false);

  Future<void> bootstrap() async {
    _status = LoadStatus.loading;
    _errorMessage = null;
    notifyListeners();

    try {
      final storedToken = await _tokenStore.read();
      final result = await _apiClient.session(storedToken);
      await _finishAuth(result.deviceToken);
    } catch (e) {
      _status = LoadStatus.error;
      _errorMessage = e.toString();
    }

    notifyListeners();
  }

  /// Shared tail of every path that ends in a usable [deviceToken] — the guest bootstrap above,
  /// and [login] below. Loads the content pack (best-effort) and the player projection, and only
  /// once the player projection actually succeeds does it commit [_deviceToken] and persist the
  /// token to storage.
  ///
  /// The order is deliberate: [deviceToken] itself is already known-good by the time this runs
  /// (session()/login() already exchanged it with the server), but *this device having a usable
  /// save* is only confirmed once `me()` succeeds. Persisting the token before that point would
  /// mean a transient failure here — network blip on `me()`, say — leaves the token saved to disk
  /// even though the caller (`login()`) reports failure to the UI. On [login] specifically that is
  /// not a cosmetic inconsistency: the player just typed a password, was told it didn't work, and
  /// would otherwise be silently logged into that very account on the next launch, because
  /// `_resolveStartup()` finds the already-written token and takes the returning-player fast path
  /// without ever having shown a success confirmation.
  Future<void> _finishAuth(String deviceToken) async {
    try {
      _contentPack = await _apiClient.contentPack();
    } catch (_) {
      // best-effort; keep null / defaults
    }

    _me = await _apiClient.me(deviceToken);

    _deviceToken = deviceToken;
    await _tokenStore.write(deviceToken);
    _status = LoadStatus.ready;

    // Read the catalog now so the upgrade advice exists on the first Home, not only after a visit
    // to the Market. Not awaited: Home paints from the player projection alone, and a failure here
    // costs nothing but the nudge (the Market re-reads when opened).
    unawaited(loadInventory());
    unawaited(_loadSeen(_me!.playerId));
  }

  /// Reads which advised upgrade this player has already been shown. Off the path to Home like the
  /// catalog read: a slow or broken store costs only the dot, never the screen.
  Future<void> _loadSeen(String playerId) async {
    try {
      final id = await _seenStore.read(playerId);
      // Opening the Market before this arrives is newer news than what was saved.
      _upgradeSeenItemId ??= id;
    } catch (_) {}
    _upgradeSeenLoaded = true;
    notifyListeners();
  }

  bool _loggingIn = false;
  String? _loginError;

  bool get loggingIn => _loggingIn;

  /// A message for [LoginScreen] to show, or null between attempts. Cleared at the start of every
  /// call to [login].
  String? get loginError => _loginError;

  /// Exchanges [username]/[password] for the account's device token and finishes auth exactly
  /// like a returning guest's [bootstrap] does from that point on — same [_finishAuth] tail, so
  /// there is only one place that logic can drift.
  ///
  /// Returns whether it succeeded. [loginError] carries a message to show on failure; the server
  /// sends only a stable reason token (today, always `invalidCredentials` — this endpoint never
  /// distinguishes an unknown username from a wrong password, deliberately), so the display text
  /// is chosen here, the same translation this codebase already does for item/property refusals.
  Future<bool> login(String username, String password) async {
    _loggingIn = true;
    _loginError = null;
    notifyListeners();

    try {
      final result = await _apiClient.login(username, password);
      await _finishAuth(result.deviceToken);
      _loggingIn = false;
      notifyListeners();
      return true;
    } on JobRefusedException {
      _loginError = 'Incorrect username or password.';
    } catch (_) {
      _loginError = 'Could not reach the server. Check your connection and try again.';
    }

    _loggingIn = false;
    notifyListeners();
    return false;
  }

  bool _registeringAccount = false;
  String? _registerAccountError;

  bool get registeringAccount => _registeringAccount;

  /// A message for [RegisterScreen] to show, or null between attempts. Cleared at the start of
  /// every call to [registerAccount].
  String? get registerAccountError => _registerAccountError;

  /// Claims the current device's player with a username/password — see
  /// [ApiClient.registerAccount]. Returns whether it succeeded; on success [me] is updated from
  /// the server's response, so `me.username` reads non-null immediately without a follow-up read.
  ///
  /// Requires an existing session ([_deviceToken] set by [bootstrap]/[login] already having run) —
  /// this screen is only ever reached from Profile, which is only ever reached once the app is
  /// past its startup gate. Returns false without calling the server if that invariant is somehow
  /// violated, the same defensive shape [loadJobs] and friends already use for "no token yet."
  Future<bool> registerAccount(String username, String password) async {
    final token = _deviceToken;
    if (token == null) return false;

    _registeringAccount = true;
    _registerAccountError = null;
    notifyListeners();

    try {
      _me = await _apiClient.registerAccount(token, username, password);
      _registeringAccount = false;
      notifyListeners();
      return true;
    } on JobRefusedException catch (e) {
      // The server's reason is a stable token, not display text (identity-session.md's own
      // convention) — translated here, the same way item/property refusals already are.
      _registerAccountError = switch (e.reason) {
        'alreadyClaimed' => 'This device already has an account.',
        'usernameInvalid' =>
          'That username isn\'t allowed — use 3-32 lowercase letters, digits, underscores or hyphens.',
        'passwordTooWeak' => 'Password must be at least 8 characters.',
        'usernameTaken' => 'That username is already taken.',
        _ => 'Could not create the account.',
      };
    } catch (_) {
      _registerAccountError = 'Could not reach the server. Check your connection and try again.';
    }

    _registeringAccount = false;
    notifyListeners();
    return false;
  }

  Future<void> refresh() async {
    final token = _deviceToken;
    if (token == null) return;
    try {
      _me = await _apiClient.me(token);
      if (_status != LoadStatus.ready) {
        _status = LoadStatus.ready;
      }
      _reloadInventoryIfLevelChanged();
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

  /// Loads the job catalog. Safe to call repeatedly; concurrent calls are collapsed unless
  /// [supersede] is set. A level-up sets it: a read already in flight was requested before the
  /// level-up and would answer with the old gating, so only the newest read's answer is applied.
  Future<void> loadJobs({bool supersede = false}) async {
    final token = _deviceToken;
    if (token == null || (_jobsLoading && !supersede)) return;

    final request = ++_jobsRequest;
    _jobsLoading = true;
    _jobsError = null;
    notifyListeners();

    try {
      final list = await _apiClient.jobs(token);
      if (request != _jobsRequest) return;
      _jobs = list.jobs;
      _levelPayoutBonus = list.levelPayoutBonus;
    } catch (e) {
      if (request == _jobsRequest) _jobsError = e.toString();
    } finally {
      if (request == _jobsRequest) {
        _jobsLoading = false;
        notifyListeners();
      }
    }
  }

  /// Performs [jobId].
  ///
  /// The server returns the full post-action player state, so this does not follow up with a /me
  /// call — that is the round trip the do-job response exists to remove. A refusal (not enough
  /// energy, level too low) is surfaced through [actionMessage] rather than as an error state,
  /// because it is normal play, not a failure.
  ///
  /// [extras] is how many extra copies of each required item the player chose to spend on top of
  /// what the job needs, keyed by item id. The server validates it and decides the resulting odds.
  Future<void> doJob(String jobId, {Map<String, int> extras = const {}}) async {
    final token = _deviceToken;
    if (token == null || _jobInFlight != null) return;

    _jobInFlight = jobId;
    _actionMessage = null;
    notifyListeners();

    try {
      final outcome = await _apiClient.doJob(token, jobId, extras: extras);

      await _holdForReveal();
      if (_disposed || _deviceToken != token) return;

      _lastJobResult = outcome.result;
      final attempted = _jobs.where((j) => j.id == jobId).firstOrNull;
      _lastJobDrama = attempted == null
          ? JobDrama.clean
          : JobDrama.of(outcome.result, attempted, leveledUp: outcome.levelUp != null);
      _haptic(!outcome.result.succeeded || _lastJobDrama == JobDrama.major
          ? HapticFeedback.heavyImpact
          : HapticFeedback.mediumImpact);
      _cue(SfxCue.forOutcome(succeeded: outcome.result.succeeded, drama: _lastJobDrama!));
      _me = outcome.player;
      if (outcome.levelUp != null) {
        _pendingLevelUp = outcome.levelUp;
      }
      _reloadInventoryIfLevelChanged();

      // The catalog carries per-job mastery and affordability, both of which just changed.
      _applyOutcomeToCatalog(outcome);

      // A level-up can unlock jobs, and the client does not work out which (see below). Ask now
      // rather than leaving the list locked until the screen's next periodic read.
      if (outcome.levelUp != null) unawaited(loadJobs(supersede: true));

      // The first finished job is when a nudge is obviously worth having, so it is when we offer.
      unawaited(_offerNotifications());
    } on JobRefusedException catch (e) {
      _setActionMessage(
        ActionScope.jobs,
        switch (e.reason) {
          'energy' => 'Not enough energy.',
          'level' => 'Your level is too low for that job.',
          'stats' => 'Your attack or defense is too low for that job.',
          'items' => 'You do not have enough of a required item for that job.',
          'extras' => 'That job does not take those extra items.',
          _ => e.detail,
        },
      );
    } catch (e) {
      _setActionMessage(ActionScope.jobs, 'Could not do that job. $e');
    } finally {
      _jobInFlight = null;
      notifyListeners();
    }
  }

  /// Holds the outcome of the job just done for [revealDelay], so the reveal has a moment of
  /// suspense to resolve. The server has already decided the result; this only delays showing it,
  /// and [skipReveal] ends it early.
  Future<void> _holdForReveal() async {
    if (revealDelay <= Duration.zero || _disposed) return;

    final hold = Completer<void>();
    _revealHold = hold;
    _anticipating = true;
    _haptic(HapticFeedback.selectionClick);
    _cue(SfxCue.riser);
    notifyListeners();

    _revealTimer = Timer(revealDelay, () {
      if (!hold.isCompleted) hold.complete();
    });
    await hold.future;

    _revealTimer?.cancel();
    _revealTimer = null;
    _revealHold = null;
    _anticipating = false;
  }

  /// Ends the anticipation hold now, so a player who taps is not made to wait.
  void skipReveal() {
    final hold = _revealHold;
    if (hold != null && !hold.isCompleted) hold.complete();
  }

  /// Fires a haptic without letting it affect the job: it is a no-op on web and in tests, and a
  /// device without a vibration motor may refuse it.
  void _haptic(Future<void> Function() cue) {
    unawaited(cue().catchError((Object _) {}));
  }

  /// Plays [cue] unless sound is off. The first cue only ever follows a tap on a job, so nothing
  /// sounds before the player has touched the page.
  void _cue(SfxCue cue) {
    if (_soundEnabled) _sfx.play(cue);
  }

  /// Turns job cues on or off and remembers the choice. Muting cuts off a cue already sounding.
  Future<void> setSoundEnabled(bool enabled) async {
    if (_soundEnabled == enabled) return;
    _soundEnabled = enabled;
    if (!enabled) _sfx.stop();
    notifyListeners();
    try {
      await _soundStore.write(enabled);
    } catch (_) {
      // The choice still holds for this session; failing to save it is not worth an error screen.
    }
  }

  /// Folds a job outcome into the cached catalog so the list reflects the new mastery, item stock
  /// and gating for the attempted job immediately, without waiting on a network round trip — all
  /// three are exact values this method can derive safely, for that one job specifically.
  ///
  /// Every *other* job's gating (level, stats, items, energy) is left exactly as it was until
  /// `JobsScreen`'s own periodic re-read asks the server again — the same ~10s cadence
  /// `PropertiesScreen` already re-reads on for its own passively-changing values.
  ///
  /// This used to also re-derive level and energy locally for every job and carry a 'stats' lock
  /// forward, on the reasoning that nothing a job attempt does can change stats (gear is untouched;
  /// a level-up grants skill points without spending them). That reasoning had a hole: the server's
  /// gate precedence is level -> stats -> items -> energy, so a job blocked by *level* never has
  /// its stats checked at all — the server simply doesn't know yet. Carrying forward
  /// `unavailableReason == 'stats'` is therefore blind to a job that was blocked by level *and*
  /// would also fail stats: the moment its level cleared, the local recompute lit its button up
  /// enabled, and the server correctly refused it.
  ///
  /// The attempted job is the one exception where recomputing is still safe, and still necessary:
  /// attempting a job only ever happens when it was already `canAttempt: true`, which means level,
  /// stats, items and energy were *all* confirmed passing the instant before this outcome arrived.
  /// Nothing about attempting a job can move level or stats. Only items (just consumed) and energy
  /// (just spent) can have changed — and both are exact, known values from this same outcome and
  /// the fresh post-action player, not a guess. Recomputing just those two, for just this one job,
  /// is what keeps a job correctly re-locking the instant its item stock runs out (e.g. the last
  /// unit of a required item consumed by the attempt itself), without resurrecting the guesswork
  /// that caused the stats bug for every other job in the list.
  void _applyOutcomeToCatalog(DoJobResult outcome) {
    final energy = _me?.energy.current;

    // Keyed by item id so this is an O(1) lookup per requirement rather than a nested scan. Empty
    // for jobs that opt into no item gate, in which case every requirement list below is empty too
    // and this is never consulted.
    final consumedByItemId = {
      for (final consumed in outcome.result.itemsConsumed) consumed.itemId: consumed,
    };

    _jobs = _jobs.map((job) {
      if (job.id != outcome.result.jobId) return job;

      final itemRequirements = job.itemRequirements.map((requirement) {
        final consumed = consumedByItemId[requirement.itemId];
        if (consumed == null) return requirement;

        return JobItemRequirement(
          itemId: requirement.itemId,
          minQuantity: requirement.minQuantity,
          maxExtras: requirement.maxExtras,
          held: consumed.quantityRemaining,
          met: consumed.quantityRemaining >= requirement.minQuantity,
        );
      }).toList(growable: false);

      final meetsItems = itemRequirements.every((requirement) => requirement.met);
      final affords = energy == null || energy >= job.energyCost;
      final reason = !meetsItems ? 'items' : (!affords ? 'energy' : null);

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
        attackRequirement: job.attackRequirement,
        defenseRequirement: job.defenseRequirement,
        itemRequirements: itemRequirements,
        successChance: job.successChance,
        extraSuccessChance: job.extraSuccessChance,
        maxSuccessChance: job.maxSuccessChance,
        mastery: outcome.result.mastery,
        canAttempt: reason == null,
        unavailableReason: reason,
      );
    }).toList(growable: false);
  }

  /// Clears the result panel.
  void dismissJobResult() {
    _lastJobResult = null;
    _lastJobDrama = null;
    notifyListeners();
  }

  /// Drops the result without notifying, for a screen being torn down: it is the only thing showing
  /// the result, and notifying mid-dispose would rebuild listeners while the tree is locked.
  void forgetJobResult() {
    _lastJobResult = null;
    _lastJobDrama = null;
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
  // Inventory & store
  // -------------------------------------------------------------------------

  /// Loads the item catalog with this player's holdings and gating folded in.
  ///
  /// Safe to call repeatedly; concurrent calls are collapsed unless [supersede] is set. An item
  /// action sets it: a read already in flight was requested before the action and will answer with
  /// the pre-action catalog, so the action needs its own read, and only the newest read's answer is
  /// applied.
  Future<void> loadInventory({bool supersede = false}) async {
    final token = _deviceToken;
    if (token == null || (_inventoryLoading && !supersede)) return;

    final request = ++_inventoryRequest;
    _inventoryLoading = true;
    _inventoryError = null;
    final levelAtRequest = _me?.level;
    _inventoryRequestedLevel = levelAtRequest;
    notifyListeners();

    try {
      final list = await _apiClient.inventory(token);
      if (request != _inventoryRequest) return;
      _inventory = list.items;
      _loadout = list.loadout;
      _inventoryLevel = levelAtRequest;
    } catch (e) {
      if (request == _inventoryRequest) _inventoryError = e.toString();
    } finally {
      if (request == _inventoryRequest) {
        _inventoryLoading = false;
        notifyListeners();
      }
    }
  }

  /// Re-reads the catalog when the player's level has moved since it was requested: a level-up can
  /// unlock items, and the client never works that out itself. Event-driven — one request per
  /// level-up, not a poll.
  void _reloadInventoryIfLevelChanged() {
    final level = _me?.level;
    // A read still in flight was requested at some level and will answer for that one, so that is
    // the picture to compare against; once nothing is in flight it is the catalog on hand.
    final baseline = _inventoryLoading ? _inventoryRequestedLevel : _inventoryLevel;
    if (level == null || baseline == null || level == baseline) return;
    unawaited(loadInventory(supersede: true));
  }

  /// Buys one copy of [itemId].
  Future<void> buyItem(String itemId) => _itemAction(
        itemId,
        (token) async {
          final result = await _apiClient.buyItem(token, itemId);
          _me = result.player;
          _loadout = result.loadout;
        },
      );

  /// Wears [itemId], displacing whatever occupied its slot.
  Future<void> equipItem(String itemId) => _itemAction(
        itemId,
        (token) async {
          final result = await _apiClient.equipItem(token, itemId);
          _me = result.player;
          _loadout = result.loadout;
        },
      );

  /// Takes [itemId] off, keeping the copies owned.
  Future<void> unequipItem(String itemId) => _itemAction(
        itemId,
        (token) async {
          final result = await _apiClient.unequipItem(token, itemId);
          _me = result.player;
          _loadout = result.loadout;
        },
      );

  /// Runs one item action, then re-reads the catalog it just invalidated.
  ///
  /// The action response carries the authoritative player and loadout, so those are applied at once
  /// and the screen updates without waiting on a second round trip. What it does *not* carry is the
  /// gating for every *other* row: buying changes the cash `canBuy` was computed against, and
  /// equipping changes which item holds the slot `canEquip` was computed against. Rather than
  /// re-derive those locally — a second copy of a server rule, free to drift — the client asks the
  /// server again. Appendix A.1.2 is what the other choice looks like once it has drifted: job cards
  /// whose buttons stay dead because the cached decision on them is never recomputed.
  ///
  /// The refresh is deliberately not conditional on the action succeeding. A refusal usually means
  /// the client's picture was already stale, which is exactly when re-reading is most useful.
  ///
  /// The in-flight guard is held across the refresh, not released when the action returns. Between
  /// those two points the rows on screen still carry the gating the server computed *before* the
  /// action, so a second action started there would be aimed at a picture the server has already
  /// moved past. Keeping the guard up closes that. The refresh also supersedes any read already in
  /// flight (the bootstrap or level-change read): that one was requested before this action and
  /// would answer with the pre-action catalog.
  Future<void> _itemAction(String itemId, Future<void> Function(String) act) async {
    final token = _deviceToken;
    if (token == null || _itemInFlight != null) return;

    _itemInFlight = itemId;
    _actionMessage = null;
    notifyListeners();

    try {
      await act(token);
    } on JobRefusedException catch (e) {
      _setActionMessage(ActionScope.items, _itemRefusalMessage(e));
    } catch (e) {
      _setActionMessage(ActionScope.items, 'Could not do that. $e');
    }

    try {
      await loadInventory(supersede: true);
    } finally {
      _itemInFlight = null;
      notifyListeners();
    }
  }

  /// Wording for the server's refusal token. The server sends a stable token, not display text.
  String _itemRefusalMessage(JobRefusedException e) => switch (e.reason) {
        'cash' => 'You cannot afford that.',
        'level' => 'Your level is too low for that item.',
        'notOwned' => 'You do not own that item.',
        'alreadyEquipped' => 'You are already wearing that.',
        'notEquipped' => 'You are not wearing that.',
        _ => e.detail,
      };

  // -------------------------------------------------------------------------
  // Properties
  // -------------------------------------------------------------------------

  /// Loads the property catalog with this player's holdings, takings and gating folded in.
  ///
  /// Safe to call repeatedly; concurrent calls are collapsed. The Properties screen calls this on
  /// a timer, which is safe precisely because the read banks nothing server-side.
  Future<void> loadProperties() async {
    final token = _deviceToken;
    if (token == null || _propertiesLoading) return;

    _propertiesLoading = true;
    _propertiesError = null;
    notifyListeners();

    try {
      final list = await _apiClient.properties(token);
      _properties = list.properties;
      _propertiesFetchedAt = _clock();
    } catch (e) {
      _propertiesError = e.toString();
    } finally {
      _propertiesLoading = false;
      notifyListeners();
    }
  }

  /// Buys one unit of [propertyId].
  Future<void> buyProperty(String propertyId) => _propertyAction(
        propertyId,
        (token) async {
          final result = await _apiClient.buyProperty(token, propertyId);
          _me = result.player;
        },
      );

  /// Banks everything [propertyId] has earned.
  Future<void> collectProperty(String propertyId) => _propertyAction(
        propertyId,
        (token) async {
          final result = await _apiClient.collectProperty(token, propertyId);
          _me = result.player;
        },
      );

  /// Runs one property action, then re-reads the catalog it just invalidated.
  ///
  /// The same shape as [_itemAction], and for the same reasons. The action response carries the
  /// authoritative player, so cash moves at once; what it cannot carry is the gating for every
  /// *other* row, and buying moves the cash all of those were priced against. The in-flight guard
  /// is held across the refresh rather than released when the action returns — the bug found in
  /// S3.3, where a second action could be aimed at rows still carrying pre-action gating, and its
  /// own refresh would then be collapsed into the one already running.
  ///
  /// Collecting is the case that makes the guard load-bearing rather than tidy: the server refuses
  /// a conflicting second collect rather than retrying it, because a retry would settle a holding
  /// whose payout the winning request had already banked.
  Future<void> _propertyAction(
      String propertyId, Future<void> Function(String) act) async {
    final token = _deviceToken;
    if (token == null || _propertyInFlight != null) return;

    _propertyInFlight = propertyId;
    _actionMessage = null;
    notifyListeners();

    try {
      await act(token);
    } on JobRefusedException catch (e) {
      _setActionMessage(ActionScope.properties, _propertyRefusalMessage(e));
    } catch (e) {
      _setActionMessage(ActionScope.properties, 'Could not do that. $e');
    }

    try {
      await loadProperties();
    } finally {
      _propertyInFlight = null;
      notifyListeners();
    }
  }

  /// Wording for the server's property tokens. The server sends a stable token, not display text.
  String _propertyRefusalMessage(JobRefusedException e) => switch (e.reason) {
        'cash' => 'You cannot afford that.',
        'level' => 'Your level is too low for that property.',
        'notOwned' => 'You do not own that property.',
        'nothingToCollect' => 'That property has not earned anything yet.',
        _ => e.detail,
      };

  // -------------------------------------------------------------------------
  // Skills
  // -------------------------------------------------------------------------

  /// Spends one skill point on [target] ('attack' or 'defense').
  ///
  /// One spend at a time: a second tap while the first is on its way would reach the server as a
  /// competing request and come back as a conflict, which reads as a failure for a spend that in
  /// fact worked.
  Future<void> allocateSkill(String target) async {
    final token = _deviceToken;
    if (token == null || _skillInFlight) return;

    _skillInFlight = true;
    _actionMessage = null;
    notifyListeners();

    try {
      _me = await _apiClient.allocateSkill(token, target);
    } on JobRefusedException catch (e) {
      _setActionMessage(
        ActionScope.skills,
        e.reason == 'noSkillPoints' ? 'No skill points to spend.' : e.detail,
      );
    } catch (e) {
      _setActionMessage(ActionScope.skills, 'Could not allocate that point. $e');
    } finally {
      _skillInFlight = false;
      notifyListeners();
    }
  }

  // -------------------------------------------------------------------------
  // Return notifications
  // -------------------------------------------------------------------------

  /// Null until the player has been asked; then their answer.
  bool? _notificationsEnabled;
  bool _notificationPreferenceLoaded = false;
  bool _notificationOfferOpen = false;
  bool _notificationOfferDeclined = false;
  bool _notificationsBlocked = false;

  /// Whether this device can show them at all — what decides if the Rap Sheet offers the setting.
  bool get notificationsAvailable => _returnNotifier.isSupported;

  /// Whether the player wants them. False until they say yes.
  bool get notificationsEnabled => _notificationsEnabled ?? false;

  /// True after the player turned them on and the OS refused. The switch stays off, and the screen
  /// can say where to fix it.
  bool get notificationsBlocked => _notificationsBlocked;

  /// Reads the saved choice. Call once at startup, before any nudge is scheduled.
  Future<void> loadNotificationPreference() async {
    if (_notificationPreferenceLoaded) return;
    try {
      _notificationsEnabled = await _notificationPreferenceStore.read();
    } catch (_) {
      // an unreadable preference reads as "never asked"
    }
    _notificationPreferenceLoaded = true;
    notifyListeners();
  }

  /// True while the screen should offer the player return reminders: a job has finished, they have
  /// never decided, and they have not said "not now" this session.
  ///
  /// This is a question to the player, not the OS. The Android 13+ permission prompt is spendable
  /// once and a refusal is effectively permanent, so it is held back until the player says yes to
  /// something they have read.
  bool get notificationOfferPending => _notificationOfferOpen;

  Future<void> _offerNotifications() async {
    if (!_returnNotifier.isSupported || _notificationOfferOpen || _notificationOfferDeclined) {
      return;
    }
    await loadNotificationPreference();
    if (_notificationsEnabled != null || _disposed) return;
    _notificationOfferOpen = true;
    notifyListeners();
  }

  /// "Yes" on the offer: closes it and spends the OS request.
  Future<void> acceptNotificationOffer() async {
    _notificationOfferOpen = false;
    notifyListeners();
    await setNotificationsEnabled(true);
  }

  /// "Not now": closes the offer for this session and records nothing, so the OS prompt is still
  /// available when the player does want it (the Rap Sheet switch is always there).
  void declineNotificationOffer() {
    _notificationOfferOpen = false;
    _notificationOfferDeclined = true;
    notifyListeners();
  }

  /// The Rap Sheet switch. Turning on asks the OS if it has not already said yes; if it refuses the
  /// switch stays off and [notificationsBlocked] says why.
  Future<void> setNotificationsEnabled(bool enabled) async {
    await loadNotificationPreference();
    _notificationsBlocked = false;

    if (enabled) {
      final granted = await _returnNotifier.requestPermission();
      _notificationsBlocked = !granted;
      await _saveNotificationChoice(granted);
    } else {
      await _saveNotificationChoice(false);
      await cancelReturnNudges();
    }
  }

  Future<void> _saveNotificationChoice(bool enabled) async {
    _notificationsEnabled = enabled;
    _notificationOfferOpen = false;
    notifyListeners();
    try {
      await _notificationPreferenceStore.write(enabled);
    } catch (_) {
      // the choice still holds for this session
    }
    // The permission is granted *after* sign-in, so without this the first backgrounding would
    // schedule energy and nothing else — refreshNudgeSnapshot declined to read until just now.
    // notifyListeners() is already silent once disposed, but a network read is not.
    if (enabled && !_disposed) await refreshNudgeSnapshot();
  }

  /// Re-reads the property listing, but only when a nudge could actually use it.
  ///
  /// The takings are the one snapshot nothing else refreshes on its own, and
  /// [scheduleReturnNudges] schedules from whatever this leaves behind — so it is worth a read on
  /// sign-in and on every resume. On a device that cannot show notifications, or for a player who
  /// has not opted in, that read banks nothing and tells nobody, so it is not made at all.
  ///
  /// The saved choice is loaded first so a preference still in flight does not read as "opted
  /// out" and skip the refresh. [loadNotificationPreference] only guards against a *completed*
  /// load, so racing the one `main.dart` starts can read the store twice — two local reads and a
  /// redundant notify, deliberately left alone rather than given machinery to prevent.
  Future<void> refreshNudgeSnapshot() async {
    await loadNotificationPreference();
    await _dropRevokedPermission();
    if (!notificationsAvailable || !notificationsEnabled) return;
    await loadProperties();
  }

  /// The player can take the permission away in system settings without telling the app, which
  /// would leave the Rap Sheet switch reading "on" for reminders that can no longer arrive. Checked
  /// wherever the app comes back or signs in. The setting becomes "off, and blocked", the same
  /// state a refusal leaves, so the same message says how to get it back.
  Future<void> _dropRevokedPermission() async {
    if (!_returnNotifier.isSupported || _notificationsEnabled != true) return;
    if (await _returnNotifier.isPermitted() || _disposed) return;
    _notificationsBlocked = true;
    await _saveNotificationChoice(false);
  }

  /// Puts the nudges worth sending on the device, replacing any earlier ones. Called as the app
  /// goes to the background, from the last snapshot it holds.
  Future<void> scheduleReturnNudges() async {
    final me = _me;
    final meFetchedAt = _meFetchedAt;
    if (!_returnNotifier.isSupported || me == null || meFetchedAt == null) return;

    final now = _clock();
    final nudges = planReturnNudges(
      enabled: notificationsEnabled,
      me: me,
      meFetchedAt: meFetchedAt,
      properties: _properties,
      propertiesFetchedAt: _propertiesFetchedAt ?? now,
      now: now,
    );
    try {
      await _returnNotifier.replaceAll(nudges);
    } catch (_) {
      // a nudge that fails to schedule is a nudge missed, never a failure
    }
  }

  /// Clears every nudge. Called when the player is back in the app, where they no longer need
  /// telling.
  Future<void> cancelReturnNudges() async {
    if (!_returnNotifier.isSupported) return;
    try {
      await _returnNotifier.cancelAll();
    } catch (_) {
      // nothing to do about a failed cancel
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _revealTimer?.cancel();
    skipReveal();
    _sfx.stop();
    stopPolling();
    super.dispose();
  }
}
