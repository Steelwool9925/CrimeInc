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

  // ---- Inventory & store ----
  List<InventoryItem> _inventory = const [];
  LoadoutStats? _loadout;
  bool _inventoryLoading = false;
  String? _inventoryError;

  /// The id of the item currently being bought or worn, so the UI can disable just that row.
  String? _itemInFlight;

  // ---- Properties ----
  List<Property> _properties = const [];
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

  /// Total completions across every job, for the Rap Sheet's "Jobs Done" stat. A pure fold over
  /// [jobs] — no new endpoint: `GET /jobs` already returns each job's `mastery.completions`.
  int get totalJobsCompleted =>
      _jobs.fold(0, (sum, job) => sum + job.mastery.completions);

  bool get jobsLoading => _jobsLoading;
  String? get jobsError => _jobsError;
  String? get jobInFlight => _jobInFlight;
  JobResult? get lastJobResult => _lastJobResult;
  LevelUp? get pendingLevelUp => _pendingLevelUp;
  String? get actionMessage => _actionMessage;

  List<InventoryItem> get inventory => _inventory;

  /// Effective combat stats. Null until the inventory has been read once — [Me] carries only the
  /// player's base stats, because building a loadout costs a query the progression endpoints do not
  /// need to pay for.
  LoadoutStats? get loadout => _loadout;

  bool get inventoryLoading => _inventoryLoading;
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
        'stats' => 'Your attack or defense is too low for that job.',
        'items' => 'You do not have enough of a required item for that job.',
        'extras' => 'That job does not take those extra items.',
        _ => e.detail,
      };
    } catch (e) {
      _actionMessage = 'Could not do that job. $e';
    } finally {
      _jobInFlight = null;
      notifyListeners();
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
    notifyListeners();
  }

  /// Drops the result without notifying, for a screen being torn down: it is the only thing showing
  /// the result, and notifying mid-dispose would rebuild listeners while the tree is locked.
  void forgetJobResult() => _lastJobResult = null;

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
  /// Safe to call repeatedly; concurrent calls are collapsed.
  Future<void> loadInventory() async {
    final token = _deviceToken;
    if (token == null || _inventoryLoading) return;

    _inventoryLoading = true;
    _inventoryError = null;
    notifyListeners();

    try {
      final list = await _apiClient.inventory(token);
      _inventory = list.items;
      _loadout = list.loadout;
    } catch (e) {
      _inventoryError = e.toString();
    } finally {
      _inventoryLoading = false;
      notifyListeners();
    }
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
  /// moved past — and `loadInventory` would collapse its refresh into the one still running, leaving
  /// that second action's own changes unread. Keeping the guard up closes both.
  Future<void> _itemAction(String itemId, Future<void> Function(String) act) async {
    final token = _deviceToken;
    if (token == null || _itemInFlight != null) return;

    _itemInFlight = itemId;
    _actionMessage = null;
    notifyListeners();

    try {
      await act(token);
    } on JobRefusedException catch (e) {
      _actionMessage = _itemRefusalMessage(e);
    } catch (e) {
      _actionMessage = 'Could not do that. $e';
    }

    try {
      await loadInventory();
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
      _actionMessage = _propertyRefusalMessage(e);
    } catch (e) {
      _actionMessage = 'Could not do that. $e';
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
