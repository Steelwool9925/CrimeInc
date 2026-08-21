import 'dart:async';

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

/// The player the buy endpoint returns: the starting 100 less the knuckles' 75.
///
/// Deliberately a different cash figure from [kFakeMe], so a test that reads cash after a purchase
/// can only pass if the client took the server's post-action player rather than keeping the one it
/// already had.
const kBoughtMe = Me(
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
  cash: 25,
  premiumCurrency: 0,
  level: 1,
  xp: 0,
  skillPoints: 0,
  attack: 10,
  defense: 10,
);

/// A loadout with nothing equipped: the player's own 10/10, matching [kFakeMe].
const kBareLoadout = LoadoutStats(
  baseAttack: 10,
  itemAttack: 0,
  totalAttack: 10,
  baseDefense: 10,
  itemDefense: 0,
  totalDefense: 10,
);

/// The same player wearing the knuckles: +4 attack.
const kArmedLoadout = LoadoutStats(
  baseAttack: 10,
  itemAttack: 4,
  totalAttack: 14,
  baseDefense: 10,
  itemDefense: 0,
  totalDefense: 10,
);

/// Level 1, 75 cash, +4 attack — affordable from the starting 100, and owned in these fixtures.
const kOwnedWeapon = InventoryItem(
  id: 'brass-knuckles',
  name: 'Brass Knuckles',
  description: 'Cheap, quiet and entirely deniable.',
  slot: 'weapon',
  levelRequirement: 1,
  price: 75,
  attack: 4,
  defense: 0,
  quantity: 1,
  equipped: false,
  canBuy: true,
  canEquip: true,
);

/// Level 1, 120 cash — owned but not worn, and above the fixture player's cash.
const kUnaffordableArmor = InventoryItem(
  id: 'leather-jacket',
  name: 'Leather Jacket',
  description: 'Scuffed in places that suggest experience.',
  slot: 'armor',
  levelRequirement: 1,
  price: 120,
  attack: 0,
  defense: 5,
  quantity: 0,
  equipped: false,
  canBuy: false,
  buyUnavailableReason: 'cash',
  canEquip: false,
  equipUnavailableReason: 'notOwned',
);

/// Level 2 — locked behind a level the fixture player has not reached.
const kLockedWeapon = InventoryItem(
  id: 'switchblade',
  name: 'Switchblade',
  description: 'Fits a pocket; ends an argument.',
  slot: 'weapon',
  levelRequirement: 2,
  price: 180,
  attack: 7,
  defense: 0,
  quantity: 0,
  equipped: false,
  canBuy: false,
  buyUnavailableReason: 'level',
  canEquip: false,
  equipUnavailableReason: 'level',
);

/// Owned, earning, and with takings on the counter — the row CP3 collects from.
///
/// The shipped starter property: level 1, 500 cash, 240/hour over a 15-minute window, so one unit
/// caps at 60 and fills at four a minute.
const kCollectableProperty = Property(
  id: 'street-corner',
  name: 'Street Corner',
  description: 'A patch of pavement nobody works without your say-so.',
  levelRequirement: 1,
  price: 500,
  incomePerHour: 240,
  accrualCapMinutes: 15,
  quantity: 1,
  pending: 30,
  cap: 60,
  canBuy: true,
  canCollect: true,
);

/// Owned but with nothing on the counter, and priced past the fixture player's cash.
///
/// Carries the two refusals a held property can show at once: 'cash' on the buy, and
/// 'nothingToCollect' on the collect.
const kEmptyProperty = Property(
  id: 'pawn-shop',
  name: 'Pawn Shop',
  description: 'Buys low, asks nothing, and keeps no paperwork worth subpoenaing.',
  levelRequirement: 2,
  price: 1800,
  incomePerHour: 300,
  accrualCapMinutes: 120,
  quantity: 1,
  pending: 0,
  cap: 600,
  canBuy: false,
  buyUnavailableReason: 'cash',
  canCollect: false,
  collectUnavailableReason: 'nothingToCollect',
);

/// Locked behind a level the fixture player has not reached, and so owned by nobody.
const kLockedProperty = Property(
  id: 'car-wash',
  name: 'Car Wash',
  description: 'Twelve dollars a wash and a great deal more than twelve dollars a day.',
  levelRequirement: 4,
  price: 6000,
  incomePerHour: 640,
  accrualCapMinutes: 240,
  quantity: 0,
  pending: 0,
  cap: 0,
  canBuy: false,
  buyUnavailableReason: 'level',
  canCollect: false,
  collectUnavailableReason: 'notOwned',
);

/// The player the buy endpoint returns: the fixture's 600 less the corner's 500.
///
/// A different cash figure from [kFakeMe], so a test reading cash after a purchase can only pass if
/// the client took the server's post-action player.
const kPropertyBoughtMe = Me(
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
  cash: 100,
  premiumCurrency: 0,
  level: 2,
  xp: 0,
  skillPoints: 0,
  attack: 10,
  defense: 10,
);

/// The player the collect endpoint returns: the fixture's 600 plus the corner's 30 takings.
const kPropertyCollectedMe = Me(
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
  cash: 630,
  premiumCurrency: 0,
  level: 2,
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
    InventoryList? inventoryResult,
    BuyItemResult? buyResult,
    EquipItemResult? equipResult,
    EquipItemResult? unequipResult,
    bool throwOnInventory = false,
    PropertyList? propertiesResult,
    BuyPropertyResult? buyPropertyResult,
    CollectPropertyResult? collectPropertyResult,
    bool throwOnProperties = false,
    this.refuseDoJobWith,
    this.refuseAllocateWith,
    this.refuseItemActionWith,
    this.refusePropertyActionWith,
  })  : _inventoryResult = inventoryResult ??
            const InventoryList(
              items: [kOwnedWeapon, kUnaffordableArmor, kLockedWeapon],
              loadout: kBareLoadout,
              cash: 100,
              level: 1,
            ),
        _buyResult = buyResult ??
            const BuyItemResult(
              itemId: 'brass-knuckles',
              cashSpent: 75,
              quantityOwned: 1,
              player: kBoughtMe,
              loadout: kBareLoadout,
            ),
        _equipResult = equipResult ??
            const EquipItemResult(
              equippedItemId: 'brass-knuckles',
              player: kFakeMe,
              loadout: kArmedLoadout,
            ),
        _unequipResult = unequipResult ??
            const EquipItemResult(
              unequippedItemId: 'brass-knuckles',
              player: kFakeMe,
              loadout: kBareLoadout,
            ),
        _throwOnInventory = throwOnInventory,
        _propertiesResult = propertiesResult ??
            const PropertyList(
              properties: [
                kCollectableProperty,
                kEmptyProperty,
                kLockedProperty,
              ],
              cash: 600,
              level: 2,
            ),
        _buyPropertyResult = buyPropertyResult ??
            const BuyPropertyResult(
              propertyId: 'street-corner',
              cashSpent: 500,
              quantityOwned: 2,
              pending: 30,
              cap: 120,
              player: kPropertyBoughtMe,
            ),
        _collectPropertyResult = collectPropertyResult ??
            const CollectPropertyResult(
              propertyId: 'street-corner',
              cashCollected: 30,
              pending: 0,
              cap: 60,
              player: kPropertyCollectedMe,
            ),
        _throwOnProperties = throwOnProperties,
        _sessionResult = sessionResult ??
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

  /// The catalog the next GET /inventory returns.
  ///
  /// Not final: a fake that served the same catalog however many items were bought or worn would be
  /// modelling a server that ignores writes, and every test about "the client re-reads after acting"
  /// would pass against a client that re-read and got nothing new. Each action below advances this
  /// the way the real endpoints would.
  InventoryList _inventoryResult;

  final BuyItemResult _buyResult;
  final EquipItemResult _equipResult;
  final EquipItemResult _unequipResult;
  final bool _throwOnInventory;

  /// The catalog the next GET /properties returns. Advanced by each action below for the same
  /// reason [_inventoryResult] is: a fake that ignored writes would let a client that never
  /// re-reads pass every test about re-reading.
  PropertyList _propertiesResult;

  final BuyPropertyResult _buyPropertyResult;
  final CollectPropertyResult _collectPropertyResult;
  final bool _throwOnProperties;

  /// When set, every property action (buy, collect) throws a [JobRefusedException] with this
  /// reason — the shape a 409 from the property endpoints takes.
  final String? refusePropertyActionWith;

  /// When set, doJob throws a [JobRefusedException] with this reason.
  final String? refuseDoJobWith;

  /// When set, allocateSkill throws a [JobRefusedException] with this reason.
  final String? refuseAllocateWith;

  /// When set, every item action (buy, equip, unequip) throws a [JobRefusedException] with this
  /// reason — the shape a 409 from the item endpoints takes.
  final String? refuseItemActionWith;

  final List<String?> sessionCalls = [];
  int meCalls = 0;
  int contentPackCalls = 0;
  int jobsCalls = 0;
  int inventoryCalls = 0;
  int propertiesCalls = 0;
  final List<String> buyPropertyCalls = [];
  final List<String> collectPropertyCalls = [];
  final List<String> doJobCalls = [];
  final List<String> allocateCalls = [];
  final List<String> buyCalls = [];
  final List<String> equipCalls = [];
  final List<String> unequipCalls = [];

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

  /// When set, every GET /inventory waits on this before returning.
  ///
  /// Lets a test hold the post-action re-read open and observe what the client permits while the
  /// catalog on screen still predates the action.
  Completer<void>? inventoryGate;

  @override
  Future<InventoryList> inventory(String deviceToken) async {
    inventoryCalls++;
    if (_throwOnInventory) throw const ApiException(500, 'inventory error');
    if (inventoryGate != null) await inventoryGate!.future;
    return _inventoryResult;
  }

  @override
  Future<BuyItemResult> buyItem(String deviceToken, String itemId) async {
    buyCalls.add(itemId);
    _refuseItemActionIfAsked();

    _advanceCatalog(
      itemId,
      loadout: _buyResult.loadout,
      cash: _buyResult.player.cash,
      quantity: _buyResult.quantityOwned,
    );

    return _buyResult;
  }

  @override
  Future<EquipItemResult> equipItem(String deviceToken, String itemId) async {
    equipCalls.add(itemId);
    _refuseItemActionIfAsked();

    _advanceCatalog(itemId, loadout: _equipResult.loadout, equipped: true);

    return _equipResult;
  }

  @override
  Future<EquipItemResult> unequipItem(String deviceToken, String itemId) async {
    unequipCalls.add(itemId);
    _refuseItemActionIfAsked();

    _advanceCatalog(itemId, loadout: _unequipResult.loadout, equipped: false);

    return _unequipResult;
  }

  void _refuseItemActionIfAsked() {
    if (refuseItemActionWith != null) {
      throw JobRefusedException(
          refuseItemActionWith!, 'refused: $refuseItemActionWith');
    }
  }

  /// When set, every GET /properties waits on this before returning.
  ///
  /// Lets a test hold the post-action re-read open and observe what the client permits while the
  /// catalog on screen still predates the action.
  Completer<void>? propertiesGate;

  @override
  Future<PropertyList> properties(String deviceToken) async {
    propertiesCalls++;
    if (_throwOnProperties) throw const ApiException(500, 'properties error');
    if (propertiesGate != null) await propertiesGate!.future;
    return _propertiesResult;
  }

  @override
  Future<BuyPropertyResult> buyProperty(
      String deviceToken, String propertyId) async {
    buyPropertyCalls.add(propertyId);
    _refusePropertyActionIfAsked();

    _advanceProperties(
      propertyId,
      cash: _buyPropertyResult.player.cash,
      quantity: _buyPropertyResult.quantityOwned,
      pending: _buyPropertyResult.pending,
      cap: _buyPropertyResult.cap,
    );

    return _buyPropertyResult;
  }

  @override
  Future<CollectPropertyResult> collectProperty(
      String deviceToken, String propertyId) async {
    collectPropertyCalls.add(propertyId);
    _refusePropertyActionIfAsked();

    _advanceProperties(
      propertyId,
      cash: _collectPropertyResult.player.cash,
      pending: _collectPropertyResult.pending,
      cap: _collectPropertyResult.cap,
    );

    return _collectPropertyResult;
  }

  void _refusePropertyActionIfAsked() {
    if (refusePropertyActionWith != null) {
      throw JobRefusedException(
          refusePropertyActionWith!, 'refused: $refusePropertyActionWith');
    }
  }

  /// Moves the stored catalog on the way the server would after an action on [propertyId].
  ///
  /// Only the fields an action can change are touched. The gating is recomputed from the new
  /// holding rather than carried across, so a screen driven by this fake sees the same states it
  /// will see in production — in particular a collect button that goes dead once the counter is
  /// empty.
  void _advanceProperties(
    String propertyId, {
    required int cash,
    int? quantity,
    int? pending,
    int? cap,
  }) {
    _propertiesResult = PropertyList(
      properties: _propertiesResult.properties.map((p) {
        if (p.id != propertyId) return p;

        final newQuantity = quantity ?? p.quantity;
        final newPending = pending ?? p.pending;
        final newCap = cap ?? p.cap;

        return Property(
          id: p.id,
          name: p.name,
          description: p.description,
          levelRequirement: p.levelRequirement,
          price: p.price,
          incomePerHour: p.incomePerHour,
          accrualCapMinutes: p.accrualCapMinutes,
          quantity: newQuantity,
          pending: newPending,
          cap: newCap,
          canBuy: p.buyUnavailableReason == 'level'
              ? false
              : cash >= p.price,
          buyUnavailableReason: p.buyUnavailableReason == 'level'
              ? 'level'
              : (cash >= p.price ? null : 'cash'),
          canCollect: newQuantity > 0 && newPending > 0,
          collectUnavailableReason: newQuantity < 1
              ? 'notOwned'
              : (newPending > 0 ? null : 'nothingToCollect'),
        );
      }).toList(growable: false),
      cash: cash,
      level: _propertiesResult.level,
    );
  }

  /// Moves the stored catalog on the way the server would after an action on [itemId].
  ///
  /// Only the fields an action can change are touched; everything else is carried across, because a
  /// fake that quietly reset unrelated fields would hide a client that had lost them.
  void _advanceCatalog(
    String itemId, {
    required LoadoutStats loadout,
    int? cash,
    int? quantity,
    bool? equipped,
  }) {
    _inventoryResult = InventoryList(
      items: _inventoryResult.items.map((i) {
        if (i.id != itemId) {
          // Equipping one item takes off whatever shared its slot — the same swap the server does.
          final displaced = equipped == true && i.slot == _slotOf(itemId) && i.equipped;
          return displaced ? _withState(i, equipped: false) : i;
        }
        return _withState(i, quantity: quantity, equipped: equipped);
      }).toList(growable: false),
      loadout: loadout,
      cash: cash ?? _inventoryResult.cash,
      level: _inventoryResult.level,
    );
  }

  String? _slotOf(String itemId) => _inventoryResult.items
      .where((i) => i.id == itemId)
      .map((i) => i.slot)
      .firstOrNull;

  static InventoryItem _withState(
    InventoryItem i, {
    int? quantity,
    bool? equipped,
  }) {
    final newQuantity = quantity ?? i.quantity;
    final newEquipped = equipped ?? i.equipped;

    return InventoryItem(
      id: i.id,
      name: i.name,
      description: i.description,
      slot: i.slot,
      levelRequirement: i.levelRequirement,
      price: i.price,
      attack: i.attack,
      defense: i.defense,
      quantity: newQuantity,
      equipped: newEquipped,
      canBuy: i.canBuy,
      buyUnavailableReason: i.buyUnavailableReason,
      // The server recomputes these against the new holding; the fake follows the same two rules
      // the real gating uses, so a screen driven by it sees the states it will see in production.
      canEquip: newQuantity > 0 && !newEquipped && i.equipUnavailableReason != 'level',
      equipUnavailableReason: newQuantity == 0
          ? 'notOwned'
          : newEquipped
              ? 'alreadyEquipped'
              : (i.equipUnavailableReason == 'level' ? 'level' : null),
    );
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
