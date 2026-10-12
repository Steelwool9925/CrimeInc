import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/accessible_name.dart';
import '../widgets/item_gating.dart';
import 'inventory_screen.dart';

/// The store — the whole item catalog, priced, with the server's buy gating rendered as-is.
///
/// Every gating decision shown here comes from the server (`canBuy`, `buyUnavailableReason`); the
/// client only renders it. Re-deriving affordability locally would risk showing an item as buyable
/// that the server then refuses — and would be a second copy of a rule free to drift the moment
/// either side is retuned.
class StoreScreen extends StatefulWidget {
  const StoreScreen({super.key, required this.appState, this.highlightItemId});

  final AppState appState;

  /// An item to bring to the player's attention: scrolled into view and marked "Your next
  /// upgrade". Set when Home's upgrade prompt opens the Market, so the nudge ends at the item and
  /// not at the top of a list.
  final String? highlightItemId;

  @override
  State<StoreScreen> createState() => _StoreScreenState();
}

class _StoreScreenState extends State<StoreScreen> {
  /// Below this width the app bar cannot hold the title, the cash and the Inventory control at once.
  static const double _narrowWidth = 360;

  final _highlightKey = GlobalKey();
  bool _revealed = false;

  @override
  void initState() {
    super.initState();
    // Load after the first frame so the screen can show its loading state.
    WidgetsBinding.instance
        .addPostFrameCallback((_) => widget.appState.loadInventory());
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) {
        final state = widget.appState;
        final cashLabel = state.contentPack?.cashLabel ?? 'Cash';

        // Beside the title there is room for the cash on a normal phone. On a very narrow one the
        // title was squeezed to "MAR…" by the cash and the labelled Inventory control, so the cash
        // moves to a line of its own under the bar and the title keeps its room.
        final narrow = MediaQuery.sizeOf(context).width < _narrowWidth;
        final cash = state.me == null
            ? null
            : Text('$cashLabel ${state.me!.cash}', key: const Key('store-cash'));

        return Scaffold(
          appBar: AppBar(
            title: const Text('Market'),
            bottom: narrow && cash != null
                ? PreferredSize(
                    preferredSize: const Size.fromHeight(28),
                    child: Align(
                      alignment: Alignment.centerRight,
                      child: Padding(padding: const EdgeInsets.only(right: 16, bottom: 6), child: cash),
                    ),
                  )
                : null,
            actions: [
              if (!narrow && cash != null)
                Padding(
                  padding: const EdgeInsets.only(right: 16),
                  child: Center(child: cash),
                ),
              // Icon and word together: an icon-only control hides behind a tooltip a phone never
              // shows, and this is the way from buying an item to wearing it.
              TextButton.icon(
                key: const Key('go-to-inventory'),
                // The theme's accent red on the ink ground is about 3.5:1 — too faint for small
                // text, and this label exists to be read.
                style: TextButton.styleFrom(
                  foregroundColor: Theme.of(context).colorScheme.onSurface,
                ),
                icon: const Icon(Icons.backpack_outlined),
                label: const Text('Inventory'),
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute(
                      builder: (_) => InventoryScreen(appState: widget.appState)),
                ),
              ),
            ],
          ),
          body: _buildBody(context, state, cashLabel),
        );
      },
    );
  }

  Widget _buildBody(BuildContext context, AppState state, String cashLabel) {
    if (state.inventoryLoading && state.inventory.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.inventoryError != null && state.inventory.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              state.inventoryError!,
              key: const Key('store-error'),
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              key: const Key('store-retry'),
              onPressed: state.loadInventory,
              child: const Text('Retry'),
            ),
          ],
        ),
      );
    }

    // The catalog is on screen: the Market has been visited, so the dot on Home can clear. And if
    // an item was asked for, bring it into view — once, so a later rebuild never yanks the list
    // back from where the player has scrolled to.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      widget.appState.markUpgradeSeen();
      final target = _highlightKey.currentContext;
      if (!_revealed && target != null) {
        _revealed = true;
        Scrollable.ensureVisible(target, alignment: 0.1, duration: const Duration(milliseconds: 300));
      }
    });

    return Column(
      children: [
        if (state.actionMessageFor(ActionScope.items) != null)
          MaterialBanner(
            key: const Key('action-message'),
            content: Text(state.actionMessageFor(ActionScope.items)!),
            actions: [
              TextButton(
                onPressed: state.clearActionMessage,
                child: const Text('Dismiss'),
              ),
            ],
          ),
        Expanded(
          // Every row is built up front, not lazily (ListView builds on demand even from a list of
          // children): a highlighted item further down has to exist to be scrolled to, and a
          // catalog is a handful of rows.
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final item in state.inventory)
                  Padding(
                    key: item.id == widget.highlightItemId ? _highlightKey : null,
                    padding: const EdgeInsets.only(bottom: 8),
                    child: _StoreCard(
                      item: item,
                      cashLabel: cashLabel,
                      busy: state.itemInFlight == item.id,
                      anyBusy: state.itemInFlight != null,
                      highlighted: item.id == widget.highlightItemId && !item.isOwned,
                      comparison: state.upgradeAdvice?.item.id == item.id
                          ? state.upgradeAdvice?.comparisonLabel
                          : null,
                      onBuy: () => state.buyItem(item.id),
                      onEquip: () => state.equipItem(item.id),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _StoreCard extends StatelessWidget {
  const _StoreCard({
    required this.item,
    required this.cashLabel,
    required this.busy,
    required this.anyBusy,
    required this.highlighted,
    required this.comparison,
    required this.onBuy,
    required this.onEquip,
  });

  final InventoryItem item;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;

  /// The item Home recommended. Marked in words and with the accent colour, never colour alone.
  final bool highlighted;

  /// What the highlighted item adds over what is worn, when that is not just its own stats.
  final String? comparison;
  final VoidCallback onBuy;
  final VoidCallback onEquip;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final enabled = item.canBuy && !anyBusy;

    return Card(
      key: Key('store-card-${item.id}'),
      shape: highlighted
          ? (theme.cardTheme.shape as RoundedRectangleBorder?)
              ?.copyWith(side: BorderSide(color: theme.colorScheme.primary, width: 3))
          : null,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (highlighted) ...[
              Row(
                key: Key('store-highlight-${item.id}'),
                children: [
                  Icon(Icons.circle, size: 10, color: theme.colorScheme.primary),
                  const SizedBox(width: 8),
                  Text('Your next upgrade', style: theme.textTheme.titleSmall),
                ],
              ),
              if (comparison != null) ...[
                const SizedBox(height: 2),
                Text(comparison!, style: theme.textTheme.bodySmall),
              ],
              const SizedBox(height: 8),
            ],
            Row(
              children: [
                Expanded(
                  child: Text(item.name, style: theme.textTheme.titleMedium),
                ),
                Text(slotLabel(item.slot), style: theme.textTheme.bodySmall),
              ],
            ),
            if (item.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(item.description, style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            Text(
              '$cashLabel ${item.price}  ·  ${statLabel(item)}',
              style: theme.textTheme.bodyMedium,
            ),
            if (item.quantity > 0) ...[
              const SizedBox(height: 4),
              Text(
                // Owning a thing and wearing it are different states, and the card used to show
                // only the first. A player who bought a weapon saw "Owned: 1", no change to their
                // attack, and nothing anywhere explaining why — the Equip action lived behind an
                // unlabelled icon in the app bar, two screens away.
                item.equipped
                    ? 'Owned: ${item.quantity}  ·  worn'
                    : 'Owned: ${item.quantity}  ·  not worn',
                key: Key('store-owned-${item.id}'),
                style: theme.textTheme.bodySmall,
              ),
            ],
            const SizedBox(height: 8),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: !item.canBuy
                      ? Text(
                          buyLockLabel(item),
                          key: Key('store-lock-${item.id}'),
                          style: theme.textTheme.bodySmall
                              ?.copyWith(color: theme.colorScheme.error),
                        )
                      : const SizedBox.shrink(),
                ),
                // Offered right here, next to Buy, for anything owned but not yet worn. Buying
                // does not equip, and gear that is not worn changes nothing at all — not the
                // player's stats and not which jobs unlock. Surfacing it on the card is what makes
                // that recoverable without knowing the app bar icon exists.
                if (item.quantity > 0 && !item.equipped) ...[
                  OutlinedButton(
                    key: Key('equip-item-${item.id}'),
                    onPressed: item.canEquip && !anyBusy ? onEquip : null,
                    // Accessible names carry the item (and the refusal) — nine buttons all named
                    // "Buy" on this screen were indistinguishable to a screen reader.
                    child: AccessibleName(
                      name: item.canEquip
                          ? 'Equip ${item.name}'
                          : 'Equip ${item.name}. Unavailable: ${equipLockLabel(item)}',
                      visible: const Text('Equip'),
                    ),
                  ),
                  const SizedBox(width: 8),
                ],
                ElevatedButton(
                  key: Key('buy-item-${item.id}'),
                  onPressed: enabled ? onBuy : null,
                  child: AccessibleName(
                    name: item.canBuy
                        ? 'Buy ${item.name}'
                        : 'Buy ${item.name}. Unavailable: ${buyLockLabel(item)}',
                    visible: busy
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Text('Buy'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
