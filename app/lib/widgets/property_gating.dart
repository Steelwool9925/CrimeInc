/// Wording for the server's property tokens, and the phrasing of a holding's rate.
///
/// The server sends a stable token, not display text, so the phrasing is the client's to choose —
/// but it must be chosen in one place, for the same reason `item_gating.dart` exists. The names are
/// prefixed rather than sharing that file's `buyLockLabel`: the two take different types and a
/// screen importing both would have to disambiguate.
library;

import '../api/models.dart';

/// Why the buy button is disabled.
String propertyBuyLockLabel(Property property) =>
    switch (property.buyUnavailableReason) {
      'level' => 'Requires level ${property.levelRequirement}',
      'cash' => 'Costs more than you have',
      _ => 'Unavailable',
    };

/// Why the collect button is disabled.
String propertyCollectLockLabel(Property property) =>
    switch (property.collectUnavailableReason) {
      'notOwned' => 'Not owned',
      'nothingToCollect' => 'Nothing on the counter yet',
      _ => 'Unavailable',
    };

/// What one unit earns, how long it takes to fill, and what a full window therefore holds.
///
/// All three figures are per unit and come straight from content, so the line means something on a
/// property nobody owns yet — which is the whole point of the store half of the screen.
///
/// The last figure is the one that matters and the one that used to be missing. "240 an hour,
/// fills in 8 hours" makes the player multiply to learn what a night is worth, and the answer only
/// appeared after buying, as the right-hand number of the owned card's meter. The decision to buy
/// is made before that, so the payoff has to be on the card at the moment of choosing.
String propertyIncomeLabel(Property property, String cashLabel) =>
    '$cashLabel ${property.incomePerHour} an hour  ·  fills in '
    '${accrualWindowLabel(property.accrualCapMinutes)}  ·  holds up to '
    '${propertyWindowCash(property)}';

/// The cash one unit holds when its window is full: rate × window, floored to whole cash.
///
/// Derived on the client because the server sends `cap: 0` for a property the player does not own
/// yet (the ceiling it reports is for the units held). Floored to match the domain's own
/// definition of a unit's cap, so the figure shown is the figure that will actually accrue.
int propertyWindowCash(Property property) =>
    property.incomePerHour * property.accrualCapMinutes ~/ 60;

/// A fill window in the largest unit that stays exact.
///
/// The shipped pack now runs from eight hours to a full day — every property banks at least a
/// night, so in practice every value takes the hours branch. The sub-hour and ragged-minute
/// branches are kept because the content schema still permits them (1 to 43,200 minutes) and a
/// pack is data: a future pack, or a rebalance, may well use them again.
///
/// "1440 min" is a number a player has to do arithmetic on before it means anything.
String accrualWindowLabel(int minutes) {
  if (minutes < 60) return '$minutes min';
  if (minutes % 60 != 0) return '$minutes min';

  final hours = minutes ~/ 60;
  return hours == 1 ? '1 hour' : '$hours hours';
}
