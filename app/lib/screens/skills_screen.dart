import 'package:flutter/material.dart';

import '../state/app_state.dart';

/// Spend unspent skill points on Attack or Defense.
///
/// The stat increase per point is a server-side balance value, so this screen shows the current
/// stats and lets the player spend — it never predicts the resulting figure.
class SkillsScreen extends StatelessWidget {
  const SkillsScreen({super.key, required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: appState,
      builder: (context, _) {
        final me = appState.me;

        return Scaffold(
          appBar: AppBar(title: const Text('Skills')),
          body: me == null
              ? const Center(child: CircularProgressIndicator())
              : Column(
                  children: [
                    if (appState.actionMessage != null)
                      MaterialBanner(
                        key: const Key('skills-message'),
                        content: Text(appState.actionMessage!),
                        actions: [
                          TextButton(
                            onPressed: appState.clearActionMessage,
                            child: const Text('Dismiss'),
                          ),
                        ],
                      ),
                    Expanded(
                      child: ListView(
                        padding: const EdgeInsets.all(16),
                        children: [
                          Card(
                            child: Padding(
                              padding: const EdgeInsets.all(16),
                              child: Row(
                                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                children: [
                                  Text(
                                    'Unspent points',
                                    style: Theme.of(context).textTheme.titleMedium,
                                  ),
                                  Text(
                                    '${me.skillPoints}',
                                    key: const Key('skill-points'),
                                    style: Theme.of(context).textTheme.titleLarge,
                                  ),
                                ],
                              ),
                            ),
                          ),
                          const SizedBox(height: 8),
                          _SkillRow(
                            label: 'Attack',
                            value: me.attack,
                            target: 'attack',
                            enabled: me.skillPoints > 0,
                            onAllocate: appState.allocateSkill,
                          ),
                          const SizedBox(height: 8),
                          _SkillRow(
                            label: 'Defense',
                            value: me.defense,
                            target: 'defense',
                            enabled: me.skillPoints > 0,
                            onAllocate: appState.allocateSkill,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
        );
      },
    );
  }
}

class _SkillRow extends StatelessWidget {
  const _SkillRow({
    required this.label,
    required this.value,
    required this.target,
    required this.enabled,
    required this.onAllocate,
  });

  final String label;
  final int value;
  final String target;
  final bool enabled;
  final Future<void> Function(String target) onAllocate;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Expanded(
              child: Text(label, style: Theme.of(context).textTheme.titleMedium),
            ),
            Text(
              '$value',
              key: Key('stat-$target'),
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(width: 16),
            IconButton.filled(
              key: Key('allocate-$target'),
              icon: const Icon(Icons.add),
              tooltip: 'Spend a point on $label',
              onPressed: enabled ? () => onAllocate(target) : null,
            ),
          ],
        ),
      ),
    );
  }
}
