import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/item_gating.dart';
import 'inventory_screen.dart';

/// The store — the whole item catalog, priced, with the server's buy gating rendered as-is.
///
/// Every gating decision shown here comes from the server (`canBuy`, `buyUnavailableReason`); the
/// client only renders it. Re-deriving affordability locally would risk showing an item as buyable
/// that the server then refuses — and would be a second copy of a rule free to drift the moment
/// either side is retuned.
class StoreScreen extends StatefulWidget {
  const StoreScreen({super.key, required this.appState});

  final AppState appState;

  @override
  State<StoreScreen> createState() => _StoreScreenState();
}

class _StoreScreenState extends State<StoreScreen> {
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

        return Scaffold(
          appBar: AppBar(
            title: const Text('Market'),
            actions: [
              if (state.me != null)
                Padding(
                  padding: const EdgeInsets.only(right: 16),
                  child: Center(
                    child: Text(
                      '$cashLabel ${state.me!.cash}',
                      key: const Key('store-cash'),
                    ),
                  ),
                ),
              IconButton(
                key: const Key('go-to-inventory'),
                icon: const Icon(Icons.backpack_outlined),
                tooltip: 'Inventory',
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
              style: const TextStyle(color: Colors.red),
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
        Expanded(
          child: ListView.builder(
            padding: const EdgeInsets.all(16),
            itemCount: state.inventory.length,
            itemBuilder: (context, index) {
              final item = state.inventory[index];
              return Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: _StoreCard(
                  item: item,
                  cashLabel: cashLabel,
                  busy: state.itemInFlight == item.id,
                  anyBusy: state.itemInFlight != null,
                  onBuy: () => state.buyItem(item.id),
                ),
              );
            },
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
    required this.onBuy,
  });

  final InventoryItem item;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onBuy;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final enabled = item.canBuy && !anyBusy;

    return Card(
      key: Key('store-card-${item.id}'),
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
              Text('Owned: ${item.quantity}', style: theme.textTheme.bodySmall),
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
                ElevatedButton(
                  key: Key('buy-item-${item.id}'),
                  onPressed: enabled ? onBuy : null,
                  child: busy
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Buy'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
