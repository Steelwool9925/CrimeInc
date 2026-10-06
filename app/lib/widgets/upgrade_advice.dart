import '../api/models.dart';

/// The one item worth telling the player about: something they can buy right now that beats what
/// they are wearing in its slot.
class UpgradeAdvice {
  const UpgradeAdvice({
    required this.item,
    required this.attackGain,
    required this.defenseGain,
    this.replaces,
  });

  final InventoryItem item;

  /// What the player is wearing in the item's slot today, or null when the slot is empty.
  final InventoryItem? replaces;

  /// How much more attack / defense the item gives than whatever occupies its slot today (the
  /// whole of its own bonus when the slot is empty). Never negative: a stat the item lowers is
  /// reported as zero gain, and an item that lowers the total is never advised at all.
  final int attackGain;
  final int defenseGain;

  /// "+7 attack, +1 defense", leaving out a stat that does not change.
  String get gainLabel {
    final parts = [
      if (attackGain > 0) '+$attackGain attack',
      if (defenseGain > 0) '+$defenseGain defense',
    ];
    return parts.join(', ');
  }

  /// "+3 attack, +1 defense more than your Brass Knuckles" — the gain *over what is worn*, which is
  /// not the item's own stats. Null for an empty slot, where the gain is the whole item.
  String? get comparisonLabel => replaces == null ? null : '$gainLabel more than your ${replaces!.name}';
}

/// Picks the cheapest affordable item that improves on what the player is wearing, or null.
///
/// Pure — no widgets, no I/O — so the badge on Home, the line on Home and any later surface read
/// one answer.
///
/// **Whether an item may be bought is the server's decision, not this function's.** `canBuy` and
/// its reason token were computed by the same check the purchase itself refuses on. The one thing
/// that goes stale between catalog reads is cash, which keeps changing (jobs, collecting), so an
/// item the server refused with `cash` counts as buyable once [Me.cash] covers its price. A `level`
/// refusal never does: the level gate is the server's, and the client re-reads the catalog when the
/// player's level changes.
///
/// "Improves" means the item's attack plus defense beats the equipped item in the same slot (or
/// beats nothing, for an empty slot). Items already owned are not advised: the answer to owning
/// something better is to wear it, which the Inventory already offers.
///
/// When several qualify, the cheapest wins — the first upgrade a player can afford is the one worth
/// naming — then the larger improvement, then the id, so the choice never flickers between reads.
UpgradeAdvice? adviseUpgrade(Me me, List<InventoryItem> items) {
  final wornBySlot = <String, InventoryItem>{
    for (final item in items)
      if (item.equipped) item.slot: item,
  };

  UpgradeAdvice? best;

  for (final item in items) {
    if (item.isOwned) continue;

    final buyableNow =
        (item.canBuy || item.buyUnavailableReason == 'cash') && me.cash >= item.price;
    if (!buyableNow) continue;

    final worn = wornBySlot[item.slot];
    final attackGain = item.attack - (worn?.attack ?? 0);
    final defenseGain = item.defense - (worn?.defense ?? 0);
    if (attackGain + defenseGain <= 0) continue;

    final candidate = UpgradeAdvice(
      item: item,
      attackGain: attackGain > 0 ? attackGain : 0,
      defenseGain: defenseGain > 0 ? defenseGain : 0,
      replaces: worn,
    );

    if (best == null || _beats(candidate, best)) best = candidate;
  }

  return best;
}

bool _beats(UpgradeAdvice a, UpgradeAdvice b) {
  if (a.item.price != b.item.price) return a.item.price < b.item.price;
  final aGain = a.attackGain + a.defenseGain;
  final bGain = b.attackGain + b.defenseGain;
  if (aGain != bGain) return aGain > bGain;
  return a.item.id.compareTo(b.item.id) < 0;
}
