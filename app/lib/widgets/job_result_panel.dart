import 'package:flutter/material.dart';

import '../api/models.dart';

/// Shows what the last job attempt actually paid — whether or not it came off.
///
/// The payout is a server-side roll from a range, so the player cannot predict it: seeing the
/// realised figure is the feedback that makes the loop readable. Since M3R an attempt can also
/// fail, and a failure is reported here rather than as an error, because it is an ordinary outcome —
/// the energy was still spent and a consolation share was still paid.
class JobResultPanel extends StatelessWidget {
  /// Type metrics for the outcome note, pinned rather than inherited so the two-line box reserved
  /// for it below is exactly the height the text will occupy. See the note's own comment.
  static const double _noteFontSize = 12;
  static const double _noteLineHeight = 1.35;

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
            // Both trailing lines below are ALWAYS laid out, even when they have nothing to say.
            //
            // This panel is inserted above the job list, so any change in its height moves every
            // "Do job" button beneath it. Previously the outcome note rendered only on a failure
            // and the mastery line only on a rank-up, which meant the panel had three different
            // heights and the primary button jumped by roughly a line between consecutive taps —
            // on the one control a player presses upwards of thirty times in an opening session.
            // Reserving the space costs a little whitespace and buys a button that stays put.
            const SizedBox(height: 8),
            // Exactly two lines are reserved whatever the note says. Reserving the *slot* is not
            // enough on its own: the failure note is longer than the success note and wraps to a
            // second line on a narrow screen while the success note stays on one, which would put
            // the jump straight back on phones. `height: 1.35` pins the line box so the
            // reservation is exact instead of depending on the font's own metrics, and the text
            // scaler is applied so the box still fits when the player has large text turned on.
            SizedBox(
              height: MediaQuery.textScalerOf(context).scale(_noteFontSize) * _noteLineHeight * 2,
              child: Text(
                result.succeeded
                    ? 'The job came off clean.'
                    : 'The job failed. You kept a small cut and the energy is gone.',
                key: const Key('job-result-note'),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodySmall
                    ?.copyWith(fontSize: _noteFontSize, height: _noteLineHeight),
              ),
            ),
            const SizedBox(height: 8),
            // `maintainSize` keeps the rank-up line's box reserved on the attempts that did not
            // rank up, so the celebratory line can appear without shoving the list.
            Visibility(
              visible: result.masteryRankedUp,
              maintainSize: true,
              maintainAnimation: true,
              maintainState: true,
              child: Text(
                'Mastery rank ${result.mastery.rank}!',
                key: const Key('job-result-mastery'),
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.bold),
              ),
            ),
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
