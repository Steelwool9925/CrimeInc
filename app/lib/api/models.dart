import 'package:flutter/foundation.dart';

@immutable
class MeterState {
  const MeterState({
    required this.current,
    required this.max,
    required this.regenSecondsPerPoint,
    this.secondsUntilNextPoint,
  });

  final int current;
  final int max;
  final int regenSecondsPerPoint;
  final int? secondsUntilNextPoint;

  factory MeterState.fromJson(Map<String, dynamic> json) => MeterState(
        current: json['current'] as int,
        max: json['max'] as int,
        regenSecondsPerPoint: json['regenSecondsPerPoint'] as int,
        secondsUntilNextPoint: json['secondsUntilNextPoint'] as int?,
      );
}

@immutable
class Me {
  const Me({
    required this.playerId,
    required this.energy,
    required this.stamina,
    required this.health,
    required this.cash,
    required this.premiumCurrency,
    required this.level,
    required this.xp,
    required this.skillPoints,
    required this.attack,
    required this.defense,
    this.username,
  });

  final String playerId;
  final MeterState energy;
  final MeterState stamina;
  final MeterState health;
  final int cash;
  final int premiumCurrency;
  final int level;
  final int xp;
  final int skillPoints;
  final int attack;
  final int defense;

  /// The account username, or null for a guest player who hasn't claimed one.
  final String? username;

  factory Me.fromJson(Map<String, dynamic> json) => Me(
        playerId: json['playerId'] as String,
        energy: MeterState.fromJson(json['energy'] as Map<String, dynamic>),
        stamina: MeterState.fromJson(json['stamina'] as Map<String, dynamic>),
        health: MeterState.fromJson(json['health'] as Map<String, dynamic>),
        cash: json['cash'] as int,
        premiumCurrency: json['premiumCurrency'] as int,
        level: json['level'] as int,
        xp: json['xp'] as int,
        skillPoints: json['skillPoints'] as int,
        attack: json['attack'] as int,
        defense: json['defense'] as int,
        username: json['username'] as String?,
      );
}

@immutable
class SessionResult {
  const SessionResult({
    required this.deviceToken,
    required this.playerId,
  });

  final String deviceToken;
  final String playerId;

  factory SessionResult.fromJson(Map<String, dynamic> json) => SessionResult(
        deviceToken: json['deviceToken'] as String,
        playerId: json['playerId'] as String,
      );
}

@immutable
class ContentPack {
  const ContentPack({
    required this.name,
    required this.cashLabel,
    required this.premiumLabel,
    required this.primaryColorHex,
    this.secondaryColorHex = '',
    this.accentColorHex = '',
    this.assets = const {},
  });

  final String name;
  final String cashLabel;
  final String premiumLabel;
  final String primaryColorHex;
  final String secondaryColorHex;
  final String accentColorHex;

  /// Logical asset key ('logo', 'icon') to the path the manifest declares for it.
  ///
  /// The path is not used by the client — `GET /content/assets/{key}` resolves it server-side.
  /// What matters here is which keys the pack declares at all, so the UI can leave out art a
  /// pack does not ship instead of requesting a URL that will 404.
  final Map<String, String> assets;

  /// Whether the manifest declares a usable file for [key].
  bool hasAsset(String key) => (assets[key] ?? '').isNotEmpty;

  factory ContentPack.fromJson(Map<String, dynamic> json) {
    final currencies = json['currencies'] as Map<String, dynamic>? ?? {};
    final cash = currencies['cash'] as Map<String, dynamic>? ?? {};
    final premium = currencies['premium'] as Map<String, dynamic>? ?? {};
    final palette = json['palette'] as Map<String, dynamic>? ?? {};
    final assets = json['assets'] as Map<String, dynamic>? ?? {};
    return ContentPack(
      name: json['name'] as String? ?? '',
      cashLabel: cash['label'] as String? ?? 'Cash',
      premiumLabel: premium['label'] as String? ?? 'Gems',
      primaryColorHex: palette['primary'] as String? ?? '#6200EE',
      secondaryColorHex: palette['secondary'] as String? ?? '',
      accentColorHex: palette['accent'] as String? ?? '',
      assets: {
        for (final entry in assets.entries)
          if (entry.value is String) entry.key: entry.value as String,
      },
    );
  }
}

@immutable
class JobMastery {
  const JobMastery({
    required this.completions,
    required this.rank,
    required this.maxRank,
    this.completionsUntilNextRank,
  });

  final int completions;
  final int rank;
  final int maxRank;

  /// Null once [maxRank] is reached.
  final int? completionsUntilNextRank;

  /// Completions required per rank, derived from what the server sent.
  ///
  /// The server does not send the threshold, but it is exactly recoverable: the next rank boundary
  /// is `(rank + 1) * perRank`, and `completionsUntilNextRank` is that boundary minus [completions].
  /// So `perRank = (completions + remaining) / (rank + 1)` — an exact integer division, since the
  /// left-hand side is that product by definition. Null at max rank, where there is no next
  /// boundary to derive from.
  int? get completionsPerRank {
    final remaining = completionsUntilNextRank;
    if (remaining == null) return null;
    return (completions + remaining) ~/ (rank + 1);
  }

  /// Progress through the current rank, 0..1, for the mastery bar.
  ///
  /// Returns 1.0 at max rank so a maxed job reads as complete rather than empty, and 0.0 when the
  /// pack disables mastery entirely ([maxRank] of 0), where no bar should be shown at all.
  double get rankProgress {
    if (maxRank <= 0) return 0.0;

    final perRank = completionsPerRank;
    if (perRank == null) return 1.0; // at max rank
    if (perRank <= 0) return 0.0;

    final into = completions - (rank * perRank);
    return (into / perRank).clamp(0.0, 1.0);
  }

  /// Whether this job is fully mastered.
  bool get isMaxRank => maxRank > 0 && rank >= maxRank;

  factory JobMastery.fromJson(Map<String, dynamic> json) => JobMastery(
        completions: json['completions'] as int,
        rank: json['rank'] as int,
        maxRank: json['maxRank'] as int,
        completionsUntilNextRank: json['completionsUntilNextRank'] as int?,
      );
}

/// One item-quantity gate on a job, from this player's point of view: the content requirement plus
/// their current holding, so the card never needs a follow-up call to explain a lock.
@immutable
class JobItemRequirement {
  const JobItemRequirement({
    required this.itemId,
    required this.minQuantity,
    required this.held,
    required this.met,
  });

  final String itemId;
  final int minQuantity;

  /// How many copies this player currently holds. Zero when they own none at all, the same as a
  /// holding with too few — the server does not distinguish "never bought one" on the wire.
  final int held;

  /// Whether [held] meets [minQuantity]. Derivable client-side, but sent anyway so the card never
  /// re-implements the server's `>=` comparison.
  final bool met;

  factory JobItemRequirement.fromJson(Map<String, dynamic> json) => JobItemRequirement(
        itemId: json['itemId'] as String,
        minQuantity: json['minQuantity'] as int,
        held: json['held'] as int,
        met: json['met'] as bool,
      );
}

@immutable
class Job {
  const Job({
    required this.id,
    required this.name,
    required this.description,
    required this.area,
    required this.energyCost,
    required this.levelRequirement,
    required this.cashMin,
    required this.cashMax,
    required this.xp,
    required this.attackRequirement,
    required this.defenseRequirement,
    this.itemRequirements = const [],
    required this.successChance,
    required this.mastery,
    required this.canAttempt,
    this.unavailableReason,
  });

  final String id;
  final String name;
  final String description;
  final String area;
  final int energyCost;
  final int levelRequirement;
  final int cashMin;
  final int cashMax;
  final int xp;

  /// Minimum *effective* attack — the player's own stat plus equipped gear.
  final int attackRequirement;

  /// Minimum effective defense.
  final int defenseRequirement;

  /// Item-quantity gates this job additionally requires, alongside the stat gate. Empty for the
  /// jobs that opt into no item gate.
  final List<JobItemRequirement> itemRequirements;

  /// This player's live chance of success, 0..1, already capped by the pack ceiling.
  ///
  /// The server computes it with the same method the attempt rolls against, so what is drawn on a
  /// card is what the attempt actually gets. Sent even for a job that cannot be attempted, so the
  /// card can show what clearing the gate would buy.
  final double successChance;

  final JobMastery mastery;
  final bool canAttempt;

  /// Stable token from the server: 'level', 'stats', 'items' or 'energy'. Null when [canAttempt] is
  /// true. The user-facing wording is the client's to choose.
  final String? unavailableReason;

  factory Job.fromJson(Map<String, dynamic> json) => Job(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        area: json['area'] as String? ?? '',
        energyCost: json['energyCost'] as int,
        levelRequirement: json['levelRequirement'] as int,
        cashMin: json['cashMin'] as int,
        cashMax: json['cashMax'] as int,
        xp: json['xp'] as int,
        attackRequirement: json['attackRequirement'] as int,
        defenseRequirement: json['defenseRequirement'] as int,
        itemRequirements: (json['itemRequirements'] as List<dynamic>? ?? [])
            .map((r) => JobItemRequirement.fromJson(r as Map<String, dynamic>))
            .toList(growable: false),
        // `as num` then toDouble: System.Text.Json writes a whole-number chance as `1`, not `1.0`,
        // and `as double` on an int is a runtime type error in Dart rather than a widening.
        successChance: (json['successChance'] as num).toDouble(),
        mastery: JobMastery.fromJson(json['mastery'] as Map<String, dynamic>),
        canAttempt: json['canAttempt'] as bool,
        unavailableReason: json['unavailableReason'] as String?,
      );
}

@immutable
class JobList {
  const JobList({
    required this.jobs,
    required this.currentEnergy,
    required this.level,
  });

  final List<Job> jobs;
  final int currentEnergy;
  final int level;

  factory JobList.fromJson(Map<String, dynamic> json) => JobList(
        jobs: (json['jobs'] as List<dynamic>)
            .map((j) => Job.fromJson(j as Map<String, dynamic>))
            .toList(growable: false),
        currentEnergy: json['currentEnergy'] as int,
        level: json['level'] as int,
      );
}

@immutable
class LevelUp {
  const LevelUp({
    required this.levelsGained,
    required this.newLevel,
    required this.skillPointsGranted,
    required this.metersRefilled,
  });

  final int levelsGained;
  final int newLevel;
  final int skillPointsGranted;
  final bool metersRefilled;

  factory LevelUp.fromJson(Map<String, dynamic> json) => LevelUp(
        levelsGained: json['levelsGained'] as int,
        newLevel: json['newLevel'] as int,
        skillPointsGranted: json['skillPointsGranted'] as int,
        metersRefilled: json['metersRefilled'] as bool,
      );
}

/// One item requirement's consumption from one applied job attempt — win or lose.
@immutable
class ItemConsumption {
  const ItemConsumption({
    required this.itemId,
    required this.quantityConsumed,
    required this.quantityRemaining,
  });

  final String itemId;

  /// Copies spent this attempt. Always 1 in this first pass.
  final int quantityConsumed;

  /// Copies left in the holding after this attempt.
  final int quantityRemaining;

  factory ItemConsumption.fromJson(Map<String, dynamic> json) => ItemConsumption(
        itemId: json['itemId'] as String,
        quantityConsumed: json['quantityConsumed'] as int,
        quantityRemaining: json['quantityRemaining'] as int,
      );
}

@immutable
class JobResult {
  const JobResult({
    required this.jobId,
    required this.succeeded,
    required this.energySpent,
    required this.cashEarned,
    required this.xpEarned,
    required this.healthLost,
    required this.mastery,
    required this.masteryRankedUp,
    this.itemsConsumed = const [],
  });

  final String jobId;

  /// Whether the attempt came off.
  ///
  /// A failed attempt still arrives here as a 200 — it spent energy and changed state, so it is an
  /// outcome rather than a refusal. On a failure [cashEarned] and [xpEarned] carry the pack's
  /// consolation fractions and [mastery] is unchanged.
  final bool succeeded;

  final int energySpent;
  final int cashEarned;
  final int xpEarned;

  /// Health deducted; always 0 on a success.
  final int healthLost;

  final JobMastery mastery;
  final bool masteryRankedUp;

  /// One entry per item requirement the job gated on, spent on this attempt — win or lose. Empty
  /// for the jobs that opt into no item gate.
  final List<ItemConsumption> itemsConsumed;

  factory JobResult.fromJson(Map<String, dynamic> json) => JobResult(
        jobId: json['jobId'] as String,
        succeeded: json['succeeded'] as bool,
        energySpent: json['energySpent'] as int,
        cashEarned: json['cashEarned'] as int,
        xpEarned: json['xpEarned'] as int,
        healthLost: json['healthLost'] as int,
        mastery: JobMastery.fromJson(json['mastery'] as Map<String, dynamic>),
        masteryRankedUp: json['masteryRankedUp'] as bool,
        itemsConsumed: (json['itemsConsumed'] as List<dynamic>? ?? [])
            .map((c) => ItemConsumption.fromJson(c as Map<String, dynamic>))
            .toList(growable: false),
      );
}

/// A player's effective combat stats, split into what they own and what their gear adds.
///
/// Both halves come from the server on every read; nothing here is computed locally. Showing the
/// split rather than only the total is what lets the Inventory screen say "Attack 14 (10 + 4)" — so
/// a player can see what comes off the moment they unequip.
@immutable
class LoadoutStats {
  const LoadoutStats({
    required this.baseAttack,
    required this.itemAttack,
    required this.totalAttack,
    required this.baseDefense,
    required this.itemDefense,
    required this.totalDefense,
  });

  final int baseAttack;
  final int itemAttack;
  final int totalAttack;
  final int baseDefense;
  final int itemDefense;
  final int totalDefense;

  factory LoadoutStats.fromJson(Map<String, dynamic> json) => LoadoutStats(
        baseAttack: json['baseAttack'] as int,
        itemAttack: json['itemAttack'] as int,
        totalAttack: json['totalAttack'] as int,
        baseDefense: json['baseDefense'] as int,
        itemDefense: json['itemDefense'] as int,
        totalDefense: json['totalDefense'] as int,
      );
}

/// One catalog item as offered to this player: the content definition, their holding, and whether
/// they may buy or wear it right now.
///
/// [canBuy] / [canEquip] and their reason tokens are the server's decisions, never re-derived here.
/// The Store and the Inventory render the same rows from different angles.
@immutable
class InventoryItem {
  const InventoryItem({
    required this.id,
    required this.name,
    required this.description,
    required this.slot,
    required this.levelRequirement,
    required this.price,
    required this.attack,
    required this.defense,
    required this.quantity,
    required this.equipped,
    required this.canBuy,
    required this.canEquip,
    this.buyUnavailableReason,
    this.equipUnavailableReason,
  });

  final String id;
  final String name;
  final String description;

  /// 'weapon', 'armor' or 'vehicle' — the vocabulary items.json uses.
  final String slot;

  final int levelRequirement;
  final int price;
  final int attack;
  final int defense;

  /// Copies owned; zero when the player owns none.
  final int quantity;

  final bool equipped;
  final bool canBuy;
  final bool canEquip;

  /// Stable token from the server: 'level' or 'cash'. Null when [canBuy] is true.
  final String? buyUnavailableReason;

  /// Stable token from the server: 'notOwned', 'level' or 'alreadyEquipped'. Null when [canEquip]
  /// is true.
  final String? equipUnavailableReason;

  /// Whether the player holds at least one copy — what puts a row on the Inventory screen.
  bool get isOwned => quantity > 0;

  factory InventoryItem.fromJson(Map<String, dynamic> json) => InventoryItem(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        slot: json['slot'] as String? ?? '',
        levelRequirement: json['levelRequirement'] as int,
        price: json['price'] as int,
        attack: json['attack'] as int,
        defense: json['defense'] as int,
        quantity: json['quantity'] as int,
        equipped: json['equipped'] as bool,
        canBuy: json['canBuy'] as bool,
        canEquip: json['canEquip'] as bool,
        buyUnavailableReason: json['buyUnavailableReason'] as String?,
        equipUnavailableReason: json['equipUnavailableReason'] as String?,
      );
}

/// Response to GET /inventory: the whole catalog plus the effective stats it produces.
@immutable
class InventoryList {
  const InventoryList({
    required this.items,
    required this.loadout,
    required this.cash,
    required this.level,
  });

  final List<InventoryItem> items;
  final LoadoutStats loadout;
  final int cash;
  final int level;

  factory InventoryList.fromJson(Map<String, dynamic> json) => InventoryList(
        items: (json['items'] as List<dynamic>)
            .map((i) => InventoryItem.fromJson(i as Map<String, dynamic>))
            .toList(growable: false),
        loadout: LoadoutStats.fromJson(json['loadout'] as Map<String, dynamic>),
        cash: json['cash'] as int,
        level: json['level'] as int,
      );
}

/// Response to a buy call. Carries the full post-action player, so no follow-up /me is needed.
@immutable
class BuyItemResult {
  const BuyItemResult({
    required this.itemId,
    required this.cashSpent,
    required this.quantityOwned,
    required this.player,
    required this.loadout,
  });

  final String itemId;
  final int cashSpent;
  final int quantityOwned;
  final Me player;
  final LoadoutStats loadout;

  factory BuyItemResult.fromJson(Map<String, dynamic> json) => BuyItemResult(
        itemId: json['itemId'] as String,
        cashSpent: json['cashSpent'] as int,
        quantityOwned: json['quantityOwned'] as int,
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
        loadout: LoadoutStats.fromJson(json['loadout'] as Map<String, dynamic>),
      );
}

/// Response to an equip or unequip call — the two differ only in which side of the swap is filled.
@immutable
class EquipItemResult {
  const EquipItemResult({
    required this.player,
    required this.loadout,
    this.equippedItemId,
    this.unequippedItemId,
  });

  /// The item now worn, or null after an unequip.
  final String? equippedItemId;

  /// The item taken off — the displaced incumbent on an equip, or the target on an unequip.
  final String? unequippedItemId;

  final Me player;
  final LoadoutStats loadout;

  factory EquipItemResult.fromJson(Map<String, dynamic> json) => EquipItemResult(
        equippedItemId: json['equippedItemId'] as String?,
        unequippedItemId: json['unequippedItemId'] as String?,
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
        loadout: LoadoutStats.fromJson(json['loadout'] as Map<String, dynamic>),
      );
}

/// Response to a do-job call. Carries the full post-action player state, so the client never needs
/// a follow-up /me after doing a job.
@immutable
class DoJobResult {
  const DoJobResult({
    required this.result,
    required this.player,
    this.levelUp,
  });

  final JobResult result;
  final Me player;
  final LevelUp? levelUp;

  factory DoJobResult.fromJson(Map<String, dynamic> json) => DoJobResult(
        result: JobResult.fromJson(json['result'] as Map<String, dynamic>),
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
        levelUp: json['levelUp'] == null
            ? null
            : LevelUp.fromJson(json['levelUp'] as Map<String, dynamic>),
      );
}

/// One catalog property as GET /properties offers it: the content definition, this player's
/// holding, what it has earned, and whether they may buy or collect right now.
@immutable
class Property {
  const Property({
    required this.id,
    required this.name,
    required this.description,
    required this.levelRequirement,
    required this.price,
    required this.incomePerHour,
    required this.accrualCapMinutes,
    required this.quantity,
    required this.pending,
    required this.cap,
    required this.canBuy,
    required this.canCollect,
    this.buyUnavailableReason,
    this.collectUnavailableReason,
  });

  final String id;
  final String name;
  final String description;
  final int levelRequirement;
  final int price;

  /// What a *single* unit earns per hour. Per unit rather than per holding, so the figure still
  /// means something on a property the player does not own yet.
  final int incomePerHour;

  /// How long one unit takes to fill, in minutes.
  final int accrualCapMinutes;

  /// Units owned; zero when the player holds none.
  final int quantity;

  /// Cash collectable at the instant the listing was served, across every unit owned.
  final int pending;

  /// The ceiling [pending] is filling toward, scaled by units owned. Zero when none are owned.
  final int cap;

  final bool canBuy;
  final bool canCollect;

  /// Stable token from the server: 'level' or 'cash'. Null when [canBuy] is true.
  final String? buyUnavailableReason;

  /// Stable token from the server: 'notOwned' or 'nothingToCollect'. Null when [canCollect] is
  /// true.
  final String? collectUnavailableReason;

  /// Whether the player holds at least one unit — what puts a counter on the card.
  bool get isOwned => quantity > 0;

  /// What the counter should read [secondsElapsed] after the listing was served.
  ///
  /// **Display only.** [pending] is stale the moment it is sent, so the screen interpolates forward
  /// at the rate the server published rather than redrawing a frozen number between polls — which
  /// is why `incomePerHour` and `accrualCapMinutes` are on the wire at all.
  ///
  /// This deliberately does *not* decide anything. Whether a property may be collected stays
  /// [canCollect], the server's answer, however far this projection has climbed: a client that
  /// enabled its own buttons from its own arithmetic would be a second copy of a server rule, free
  /// to drift the moment either side is retuned.
  ///
  /// The floor matches the server's: a part-earned coin is not a coin, so the player is shown
  /// slightly less than they have earned rather than slightly more.
  int projectedPending(int secondsElapsed) {
    if (quantity < 1) return 0;
    if (pending >= cap) return cap;

    final earned = incomePerHour * quantity * secondsElapsed ~/ 3600;
    final projected = pending + earned;

    return projected > cap ? cap : projected;
  }

  factory Property.fromJson(Map<String, dynamic> json) => Property(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        levelRequirement: json['levelRequirement'] as int,
        price: json['price'] as int,
        incomePerHour: json['incomePerHour'] as int,
        accrualCapMinutes: json['accrualCapMinutes'] as int,
        quantity: json['quantity'] as int,
        pending: json['pending'] as int,
        cap: json['cap'] as int,
        canBuy: json['canBuy'] as bool,
        canCollect: json['canCollect'] as bool,
        buyUnavailableReason: json['buyUnavailableReason'] as String?,
        collectUnavailableReason: json['collectUnavailableReason'] as String?,
      );
}

/// Response to GET /properties: the whole catalog, held or not.
///
/// Carries every property in the pack for the same reason [InventoryList] does — the buy half and
/// the holdings half of the screen are two views of one list, so a single call serves both and they
/// cannot disagree about a property.
@immutable
class PropertyList {
  const PropertyList({
    required this.properties,
    required this.cash,
    required this.level,
  });

  final List<Property> properties;

  /// The cash the affordability gating was computed against.
  final int cash;

  /// The level the gating was computed against.
  final int level;

  factory PropertyList.fromJson(Map<String, dynamic> json) => PropertyList(
        properties: (json['properties'] as List<dynamic>)
            .map((p) => Property.fromJson(p as Map<String, dynamic>))
            .toList(growable: false),
        cash: json['cash'] as int,
        level: json['level'] as int,
      );
}

/// Response to a property purchase. Carries the full post-action player, so no follow-up /me is
/// needed.
@immutable
class BuyPropertyResult {
  const BuyPropertyResult({
    required this.propertyId,
    required this.cashSpent,
    required this.quantityOwned,
    required this.pending,
    required this.cap,
    required this.player,
  });

  final String propertyId;
  final int cashSpent;

  /// Units held after the purchase.
  final int quantityOwned;

  /// Cash collectable straight after the purchase — normally what the existing units had already
  /// banked, since buying settles them at the old rate first.
  final int pending;

  /// The ceiling after the purchase, scaled by the new quantity.
  final int cap;

  final Me player;

  factory BuyPropertyResult.fromJson(Map<String, dynamic> json) =>
      BuyPropertyResult(
        propertyId: json['propertyId'] as String,
        cashSpent: json['cashSpent'] as int,
        quantityOwned: json['quantityOwned'] as int,
        pending: json['pending'] as int,
        cap: json['cap'] as int,
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
      );
}

/// Response to a collection. [player] carries the cash figure CP3 watches rise.
@immutable
class CollectPropertyResult {
  const CollectPropertyResult({
    required this.propertyId,
    required this.cashCollected,
    required this.pending,
    required this.cap,
    required this.player,
  });

  final String propertyId;
  final int cashCollected;

  /// What is still pending straight after the collection — normally zero.
  final int pending;

  /// The ceiling the counter starts filling toward again.
  final int cap;

  final Me player;

  factory CollectPropertyResult.fromJson(Map<String, dynamic> json) =>
      CollectPropertyResult(
        propertyId: json['propertyId'] as String,
        cashCollected: json['cashCollected'] as int,
        pending: json['pending'] as int,
        cap: json['cap'] as int,
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
      );
}
