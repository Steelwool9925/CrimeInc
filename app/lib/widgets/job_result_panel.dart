import 'package:flutter/material.dart';

import '../api/models.dart';

/// Shows what the last job attempt actually paid.
///
/// The payout is a server-side roll from a range, so the player cannot predict it — seeing the
/// realised figure is the feedback that makes the loop readable.
class JobResultPanel extends StatelessWidget {
  const JobResultPanel({
    super.key,
    required this.result,
    required this.cashLabel,
    required this.onDismiss,
  });

  final JobResult result;
  final String cashLabel;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      key: const Key('job-result-panel'),
      color: theme.colorScheme.surfaceContainerHighest,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text('Job complete', style: theme.textTheme.titleMedium),
                ),
                IconButton(
                  key: const Key('dismiss-job-result'),
                  icon: const Icon(Icons.close),
                  tooltip: 'Dismiss',
                  onPressed: onDismiss,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 16,
              runSpacing: 4,
              children: [
                _Stat(label: cashLabel, value: '+${result.cashEarned}'),
                _Stat(label: 'XP', value: '+${result.xpEarned}'),
                _Stat(label: 'Energy', value: '-${result.energySpent}'),
              ],
            ),
            if (result.masteryRankedUp) ...[
              const SizedBox(height: 8),
              Text(
                'Mastery rank ${result.mastery.rank}!',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.bold),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        Text(value, style: theme.textTheme.titleMedium),
      ],
    );
  }
}
