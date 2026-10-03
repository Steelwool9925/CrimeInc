import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/property_gating.dart';

/// Properties — the whole catalog, with what each holding has banked drawn filling toward its cap.
///
/// Every gating decision shown here is the server's (`canBuy`, `canCollect` and their reason
/// tokens); the client only renders it. The one thing the client computes is the figure on the
/// counter between polls, and that is display only: a holding's takings are stale the instant they
/// are sent, so the card interpolates forward at the rate the server published rather than showing
/// a frozen number. It never decides from that arithmetic whether collecting is allowed.
class PropertiesScreen extends StatefulWidget {
  const PropertiesScreen({
    super.key,
    required this.appState,
    this.refreshInterval = const Duration(seconds: 10),
    this.tickInterval = const Duration(seconds: 1),
  });

  final AppState appState;

  /// How often the catalog is re-read.
  ///
  /// The projection can draw a counter climbing, but only the server can say when that turns into a
  /// collectable balance, so the screen keeps asking while it is open. The read banks nothing, which
  /// is what makes polling it safe.
  final Duration refreshInterval;

  /// How often the drawn figure is advanced between reads.
  final Duration tickInterval;

  @override
  State<PropertiesScreen> createState() => _PropertiesScreenState();
}

class _PropertiesScreenState extends State<PropertiesScreen> {
  Timer? _ticker;
  Timer? _refresher;

  /// Seconds since the catalog on screen was served — the offset the counters are drawn forward by.
  int _secondsSinceRead = 0;

  /// The list the counters are being drawn against, so a fresh one can restart the offset.
  List<Property>? _served;

  @override
  void initState() {
    super.initState();
    widget.appState.addListener(_onStateChanged);

    // Load after the first frame so the screen can show its loading state.
    WidgetsBinding.instance
        .addPostFrameCallback((_) => widget.appState.loadProperties());

    _ticker = Timer.periodic(widget.tickInterval, (_) {
      setState(() => _secondsSinceRead += widget.tickInterval.inSeconds);
    });

    _refresher = Timer.periodic(widget.refreshInterval, (_) {
      // Never while an action is mid-flight: that action holds its own guard across its re-read,
      // and a poll landing inside it would be collapsed into that read and lost.
      if (widget.appState.propertyInFlight == null) {
        widget.appState.loadProperties();
      }
    });
  }

  /// Restarts the offset whenever a newly-served catalog arrives.
  ///
  /// Identity rather than equality: every load replaces the list wholesale, so a new instance is
  /// exactly the signal that the figures on it are freshly accurate.
  void _onStateChanged() {
    final served = widget.appState.properties;
    if (!identical(served, _served)) {
      _served = served;
      _secondsSinceRead = 0;
    }
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _refresher?.cancel();
    widget.appState.removeListener(_onStateChanged);
    super.dispose();
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
            title: const Text('Properties'),
            actions: [
              if (state.me != null)
                Padding(
                  padding: const EdgeInsets.only(right: 16),
                  child: Center(
                    child: Text(
                      '$cashLabel ${state.me!.cash}',
                      key: const Key('properties-cash'),
                    ),
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
    if (state.propertiesLoading && state.properties.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.propertiesError != null && state.properties.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              state.propertiesError!,
              key: const Key('properties-error'),
              textAlign: TextAlign.center,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              key: const Key('properties-retry'),
              onPressed: state.loadProperties,
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
            itemCount: state.properties.length,
            itemBuilder: (context, index) {
              final property = state.properties[index];
              return Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: _PropertyCard(
                  property: property,
                  cashLabel: cashLabel,
                  secondsSinceRead: _secondsSinceRead,
                  busy: state.propertyInFlight == property.id,
                  anyBusy: state.propertyInFlight != null,
                  onBuy: () => state.buyProperty(property.id),
                  onCollect: () => state.collectProperty(property.id),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _PropertyCard extends StatelessWidget {
  const _PropertyCard({
    required this.property,
    required this.cashLabel,
    required this.secondsSinceRead,
    required this.busy,
    required this.anyBusy,
    required this.onBuy,
    required this.onCollect,
  });

  final Property property;
  final String cashLabel;
  final int secondsSinceRead;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onBuy;
  final VoidCallback onCollect;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pending = property.projectedPending(secondsSinceRead);

    return Card(
      key: Key('property-card-${property.id}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(property.name, style: theme.textTheme.titleMedium),
                ),
                Text('Level ${property.levelRequirement}',
                    style: theme.textTheme.bodySmall),
              ],
            ),
            if (property.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(property.description, style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            Text('$cashLabel ${property.price}',
                style: theme.textTheme.bodyMedium),
            const SizedBox(height: 2),
            Text(
              propertyIncomeLabel(property, cashLabel),
              key: Key('property-income-${property.id}'),
              style: theme.textTheme.bodySmall,
            ),
            if (property.isOwned) ...[
              const SizedBox(height: 8),
              Text('Owned: ${property.quantity}',
                  style: theme.textTheme.bodySmall),
              const SizedBox(height: 4),
              LinearProgressIndicator(
                key: Key('property-bar-${property.id}'),
                value: property.cap > 0 ? pending / property.cap : 0,
              ),
              const SizedBox(height: 4),
              Text(
                '$pending / ${property.cap}',
                key: Key('property-pending-${property.id}'),
                style: theme.textTheme.bodySmall,
              ),
            ],
            const SizedBox(height: 8),
            _locks(theme),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                if (property.isOwned) ...[
                  FilledButton(
                    key: Key('collect-property-${property.id}'),
                    onPressed: property.canCollect && !anyBusy ? onCollect : null,
                    child: busy
                        ? const _ButtonSpinner()
                        : const Text('Collect'),
                  ),
                  const SizedBox(width: 8),
                ],
                ElevatedButton(
                  key: Key('buy-property-${property.id}'),
                  onPressed: property.canBuy && !anyBusy ? onBuy : null,
                  child: busy ? const _ButtonSpinner() : const Text('Buy'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  /// The reasons the two buttons are dead, when they are.
  ///
  /// A collect lock is only worth showing on a property the player actually holds — "Not owned"
  /// under a property being offered for sale states the obvious.
  Widget _locks(ThemeData theme) {
    final style =
        theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (!property.canBuy)
          Text(
            propertyBuyLockLabel(property),
            key: Key('property-buy-lock-${property.id}'),
            style: style,
          ),
        if (property.isOwned && !property.canCollect)
          Text(
            propertyCollectLockLabel(property),
            key: Key('property-collect-lock-${property.id}'),
            style: style,
          ),
      ],
    );
  }
}

class _ButtonSpinner extends StatelessWidget {
  const _ButtonSpinner();

  @override
  Widget build(BuildContext context) => const SizedBox(
        width: 16,
        height: 16,
        child: CircularProgressIndicator(strokeWidth: 2),
      );
}
