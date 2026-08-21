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

  List<Job> get jobs => _jobs;
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
        'stats' => 'Your attack or defense is too low for that job.',
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

      // The stats gate is carried across rather than re-derived. The client does not know the
      // player's effective stats here — that needs the loadout, which /jobs does not return — and
      // nothing a job attempt does can change them: gear is untouched, and a level-up grants skill
      // points without spending them. Recomputing from level and energy alone would silently clear
      // a 'stats' lock and light up a button the server will refuse.
      final wasStatLocked = job.unavailableReason == 'stats';

      final reason = !meetsLevel
          ? 'level'
          : wasStatLocked
              ? 'stats'
              : (!affords ? 'energy' : null);

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
        successChance: job.successChance,
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
