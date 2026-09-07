import 'package:flutter/material.dart';

import '../api/models.dart';

/// Shows what the last job attempt actually paid — whether or not it came off.
///
/// The payout is a server-side roll from a range, so the player cannot predict it: seeing the
/// realised figure is the feedback that makes the loop readable. Since M3R an attempt can also
/// fail, and a failure is reported here rather than as an error, because it is an ordinary outcome —
/// the energy was still spent and a consolation share was still paid.
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
                  child: Text(
                    result.succeeded ? 'Job complete' : 'It went wrong',
                    key: const Key('job-result-title'),
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: result.succeeded ? null : theme.colorScheme.error,
                    ),
                  ),
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
                // Only ever non-zero on a failure, so the tile appears exactly when it means
                // something rather than reading "Health -0" after every successful job.
                if (result.healthLost > 0)
                  _Stat(
                    key: const Key('job-result-health'),
                    label: 'Health',
                    value: '-${result.healthLost}',
                  ),
                // One tile per item requirement the job gated on — spent whether the attempt won or
                // lost, since it is a one-time-use cost of the attempt itself, not of success.
                for (final consumed in result.itemsConsumed)
                  _Stat(
                    key: Key('job-result-item-${consumed.itemId}'),
                    label: consumed.itemId,
                    value: '-${consumed.quantityConsumed} (${consumed.quantityRemaining} left)',
                  ),
              ],
            ),
            if (!result.succeeded) ...[
              const SizedBox(height: 8),
              Text(
                'The job failed. You kept a small cut and the energy is gone.',
                key: const Key('job-result-failed-note'),
                style: theme.textTheme.bodySmall,
              ),
            ],
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
  const _Stat({super.key, required this.label, required this.value});

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
