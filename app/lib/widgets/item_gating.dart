/// Wording for the server's item tokens, shared by the Store and the Inventory.
///
/// The server sends a stable token, not display text, so the phrasing is the client's to choose —
/// but it must be chosen in exactly one place. Two screens rendering the same refusal in different
/// words is how a player learns to distrust both.
library;

import '../api/models.dart';

/// Player-facing name for a loadout slot.
String slotLabel(String slot) => switch (slot) {
      'weapon' => 'Weapon',
      'armor' => 'Armor',
      'vehicle' => 'Vehicle',
      _ => slot,
    };

/// What an item contributes, phrased for whichever stat it actually moves.
///
/// Items in the shipped pack move one stat or the other, but content is free to change that, so
/// both are shown when both are non-zero rather than silently dropping one.
String statLabel(InventoryItem item) {
  final parts = <String>[
    if (item.attack > 0) '+${item.attack} Attack',
    if (item.defense > 0) '+${item.defense} Defense',
  ];
  return parts.isEmpty ? 'No combat bonus' : parts.join('  ·  ');
}

/// Why the buy button is disabled.
String buyLockLabel(InventoryItem item) => switch (item.buyUnavailableReason) {
      'level' => 'Requires level ${item.levelRequirement}',
      'cash' => 'Costs more than you have',
      _ => 'Unavailable',
    };

/// Why the equip button is disabled.
String equipLockLabel(InventoryItem item) => switch (item.equipUnavailableReason) {
      'level' => 'Requires level ${item.levelRequirement}',
      'notOwned' => 'Not owned',
      'alreadyEquipped' => 'Already worn',
      _ => 'Unavailable',
    };
