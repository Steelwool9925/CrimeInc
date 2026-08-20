import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/item_gating.dart';

/// The inventory — what the player owns, what they are wearing, and the stats that produces.
///
/// The effective-stat card at the top is where the gear loop pays off: equipping moves the total,
/// and the split underneath shows how much of it would come off again. Those numbers come from the
/// server's loadout on every read; nothing is aggregated here.
class InventoryScreen extends StatefulWidget {
  const InventoryScreen({super.key, required this.appState});

  final AppState appState;

  @override
  State<InventoryScreen> createState() => _InventoryScreenState();
}

class _InventoryScreenState extends State<InventoryScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance
        .addPostFrameCallback((_) => widget.appState.loadInventory());
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) {
        final state = widget.appState;

        return Scaffold(
          appBar: AppBar(title: const Text('Inventory')),
          body: _buildBody(context, state),
        );
      },
    );
  }

  Widget _buildBody(BuildContext context, AppState state) {
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
              key: const Key('inv-error'),
              textAlign: TextAlign.center,
              style: const TextStyle(color: Colors.red),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              key: const Key('inv-retry'),
              onPressed: state.loadInventory,
              child: const Text('Retry'),
            ),
          ],
        ),
      );
    }

    final owned = state.ownedItems;

    return Column(
      children: [
        if (state.actionMessage != null)
          MaterialBanner(
            key: const Key('action-message'),
            content: Text(state.actionMessage!),
            actions: [
              TextButton(
                onPressed: state.clearActionMessage,
                child: const Text('Dismiss'),
              ),
            ],
          ),
        if (state.loadout != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
            child: _LoadoutCard(loadout: state.loadout!),
          ),
        Expanded(
          child: owned.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Text(
                      'You own nothing yet. Visit the Store to buy your first piece of gear.',
                      key: const Key('inv-empty'),
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ),
                )
              : ListView.builder(
                  padding: const EdgeInsets.all(16),
                  itemCount: owned.length,
                  itemBuilder: (context, index) {
                    final item = owned[index];
                    return Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: _InventoryCard(
                        item: item,
                        busy: state.itemInFlight == item.id,
                        anyBusy: state.itemInFlight != null,
                        onEquip: () => state.equipItem(item.id),
                        onUnequip: () => state.unequipItem(item.id),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

/// Effective attack and defense, with the gear contribution broken out.
class _LoadoutCard extends StatelessWidget {
  const _LoadoutCard({required this.loadout});

  final LoadoutStats loadout;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Expanded(
              child: _Stat(
                statKey: const Key('inv-attack'),
                label: 'Attack',
                total: loadout.totalAttack,
                base: loadout.baseAttack,
                fromItems: loadout.itemAttack,
                theme: theme,
              ),
            ),
            Expanded(
              child: _Stat(
                statKey: const Key('inv-defense'),
                label: 'Defense',
                total: loadout.totalDefense,
                base: loadout.baseDefense,
                fromItems: loadout.itemDefense,
                theme: theme,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({
    required this.statKey,
    required this.label,
    required this.total,
    required this.base,
    required this.fromItems,
    required this.theme,
  });

  final Key statKey;
  final String label;
  final int total;
  final int base;
  final int fromItems;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Column(
      key: statKey,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        Text('$total', style: theme.textTheme.headlineSmall),
        // Only shown when gear is actually contributing: "10 + 0" would be noise.
        if (fromItems > 0)
          Text('$base + $fromItems', style: theme.textTheme.bodySmall),
      ],
    );
  }
}

class _InventoryCard extends StatelessWidget {
  const _InventoryCard({
    required this.item,
    required this.busy,
    required this.anyBusy,
    required this.onEquip,
    required this.onUnequip,
  });

  final InventoryItem item;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onEquip;
  final VoidCallback onUnequip;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      key: Key('inv-card-${item.id}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(item.name, style: theme.textTheme.titleMedium),
                ),
                Text(slotLabel(item.slot), style: theme.textTheme.bodySmall),
              ],
            ),
            const SizedBox(height: 8),
            Text(statLabel(item), style: theme.textTheme.bodyMedium),
            if (item.quantity > 1) ...[
              const SizedBox(height: 4),
              Text('Owned: ${item.quantity}', style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                if (item.equipped)
                  Text(
                    'Worn',
                    key: Key('inv-worn-${item.id}'),
                    style: theme.textTheme.bodySmall
                        ?.copyWith(fontWeight: FontWeight.bold),
                  )
                else if (!item.canEquip)
                  Text(
                    equipLockLabel(item),
                    key: Key('inv-lock-${item.id}'),
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: theme.colorScheme.error),
                  )
                else
                  const SizedBox.shrink(),
                _actionButton(),
              ],
            ),
          ],
        ),
      ),
    );
  }

  /// Equip and unequip are one button in two states: an item is either worn or it is not, and
  /// offering both at once would ask the player to work out which one applies.
  Widget _actionButton() {
    final spinner = busy
        ? const SizedBox(
            width: 16,
            height: 16,
            child: CircularProgressIndicator(strokeWidth: 2),
          )
        : null;

    if (item.equipped) {
      return OutlinedButton(
        key: Key('unequip-item-${item.id}'),
        onPressed: anyBusy ? null : onUnequip,
        child: spinner ?? const Text('Unequip'),
      );
    }

    return ElevatedButton(
      key: Key('equip-item-${item.id}'),
      onPressed: (item.canEquip && !anyBusy) ? onEquip : null,
      child: spinner ?? const Text('Equip'),
    );
  }
}
