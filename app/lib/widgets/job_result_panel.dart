import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme/pack_theme.dart';
import 'halftone_wash.dart';
import 'job_drama.dart';
import 'job_reveal.dart';
import 'speedline_divider.dart';

/// Shows what the last job attempt actually paid — whether or not it came off.
///
/// The payout is a server-side roll from a range, so the player cannot predict it: seeing the
/// realised figure is the feedback that makes the loop readable. Since M3R an attempt can also
/// fail, and a failure is reported here rather than as an error, because it is an ordinary outcome —
/// the energy was still spent and a consolation share was still paid.
///
/// The outcome is *revealed* rather than shown. Success rises into place with a slight overshoot in
/// bone; how much more happens depends on how big a win it was ([JobDrama]): a scrape is the verdict
/// alone, a good win adds staggered stats and speedlines firing outward, and a major win adds a
/// halftone flash. Failure drops and stops dead in the error red under a heavier halftone. The two differ by direction, value and easing, never by hue alone, so they read
/// differently with colour taken away entirely. The numbers count up from zero. With animations
/// disabled in the OS the panel goes straight to its final frame.
///
/// With no [result] it draws an empty panel of exactly the same height, hidden but laid out. The job
/// list sits directly under this panel, so one that appeared from nothing moved every "Do job"
/// button down by its own height on a session's first attempt; the placeholder means the list never
/// moves. It is the same widget tree rather than a separate box so the heights agree by
/// construction instead of by a number someone has to keep in step.
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
    this.drama = JobDrama.clean,
    this.anticipating = false,
    this.onSkip,
    this.onRevealed,
  });

  final JobResult? result;
  final String cashLabel;
  final VoidCallback onDismiss;

  /// How big a win [result] was. Only ever raises the staging of a success; a failure has its own.
  final JobDrama drama;

  /// An outcome has arrived but is being held back for a beat. The panel shows that suspense in
  /// place of any earlier result, and a tap on it calls [onSkip].
  final bool anticipating;
  final VoidCallback? onSkip;

  /// Called once per result, when its reveal has finished (or at once with animations disabled).
  /// The screen holds the level-up dialog until then so it crowns the payout instead of covering it.
  final ValueChanged<JobResult>? onRevealed;

  @override
  Widget build(BuildContext context) {
    final result = anticipating ? null : this.result;
    final motion = PackMotion.of(context);
    return JobReveal<JobResult>(
      subject: result,
      duration: result == null
          ? Duration.zero
          : motion.totalDuration(succeeded: result.succeeded, stampOnly: _plainWin(result)),
      onRevealed: onRevealed,
      builder: (context, t) => _frame(context, motion, result: result, t: t),
    );
  }

  bool _plainWin(JobResult result) => result.succeeded && drama == JobDrama.clean;

  /// One frame of the panel at reveal progress [t] (0..1 across the whole reveal).
  Widget _frame(BuildContext context, PackMotion motion,
      {required JobResult? result, required double t}) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isPlaceholder = result == null;
    final succeeded = result?.succeeded ?? true;

    final plainWin = result != null && _plainWin(result);
    final totalMs =
        motion.totalDuration(succeeded: succeeded, stampOnly: plainWin).inMilliseconds;
    final stampMs = motion.stampDuration(succeeded: succeeded).inMilliseconds;
    final stampT = isPlaceholder ? 0.0 : (t * totalMs / stampMs).clamp(0.0, 1.0);
    // A plain win stages nothing after the stamp: its numbers are simply there.
    final statsT = isPlaceholder
        ? 0.0
        : plainWin
            ? 1.0
            : ((t * totalMs - stampMs) / motion.statsDuration.inMilliseconds).clamp(0.0, 1.0);

    // Success rises from below with an overshoot; failure drops from above and stops dead.
    final stampOffset = isPlaceholder
        ? 0.0
        : succeeded
            ? (1 - motion.successCurve.transform(stampT)) * 14
            : -(1 - motion.failureCurve.transform(stampT)) * 14;

    final tiles = <_StatSpec>[
      _StatSpec(label: cashLabel, prefix: '+', amount: result?.cashEarned ?? 0),
      _StatSpec(label: 'XP', prefix: '+', amount: result?.xpEarned ?? 0),
      _StatSpec(label: 'Energy', prefix: '-', amount: result?.energySpent ?? 0),
      // Only ever non-zero on a failure, so the tile appears exactly when it means
      // something rather than reading "Health -0" after every successful job.
      if (result != null && result.healthLost > 0)
        _StatSpec(
          key: const Key('job-result-health'),
          label: 'Health',
          prefix: '-',
          amount: result.healthLost,
        ),
      // One tile per item the attempt used up — spent whether the attempt won or lost,
      // since it is a cost of the attempt itself, not of success. Worded as "Used up"
      // rather than showing a bare "-1": losing a weapon is not a stat to skim past.
      // `quantityRemaining` is spare copies, because the worn one is never spent.
      if (result != null)
        for (final consumed in result.itemsConsumed)
          _StatSpec(
            key: Key('job-result-item-${consumed.itemId}'),
            label: 'Used up',
            text: '${countedItem(consumed.quantityConsumed, consumed.itemId)} '
                '(${consumed.quantityRemaining} spare left)',
            emphasised: true,
          ),
    ];
    final step = 0.5 / tiles.length;
    double tileProgress(int i) =>
        ((statsT - i * step) / (1 - (tiles.length - 1) * step)).clamp(0.0, 1.0);

    final String title;
    final String note;
    if (anticipating) {
      title = 'Moment of truth';
      note = 'The job is done. Waiting to see how it went.';
    } else if (isPlaceholder) {
      title = 'Last job';
      note = 'Your latest job shows up here.';
    } else if (succeeded) {
      title = 'Job complete';
      note = 'The job came off clean.';
    } else {
      title = 'It went wrong';
      note = 'The job failed. You kept a small cut and the energy is gone.';
    }

    final verdictStyle = theme.textTheme.headlineSmall?.copyWith(
      fontSize: 30,
      letterSpacing: 1.5,
      color: isPlaceholder
          ? scheme.onSurfaceVariant
          : (succeeded ? scheme.onSurface : scheme.error),
    );

    final card = Card(
      key: Key(anticipating
          ? 'job-result-anticipating'
          : (isPlaceholder ? 'job-result-placeholder' : 'job-result-panel')),
      color: isPlaceholder ? null : scheme.surfaceContainerHighest,
      child: Stack(
        children: [
          // A major win's flash: bone dots that swell and fade, gone by the end of the reveal.
          if (!isPlaceholder && succeeded && drama == JobDrama.major)
            Positioned.fill(
              child: HalftoneWash(
                key: const Key('job-result-flash'),
                color: scheme.onSurface,
                opacity: 0.4 * math.sin(math.pi * t),
                dotRadius: 1.6,
              ),
            ),
          if (!isPlaceholder && !succeeded)
            Positioned.fill(
              child: HalftoneWash(
                key: const Key('job-result-halftone'),
                color: scheme.tertiary,
                opacity: 0.12 + 0.3 * Curves.easeIn.transform(stampT),
                dotRadius: 1.4,
              ),
            ),
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Opacity(
                        opacity: isPlaceholder ? 1 : (stampT * 2).clamp(0.0, 1.0),
                        child: Transform.translate(
                          offset: Offset(0, stampOffset),
                          child: Text(title, key: const Key('job-result-title'), style: verdictStyle),
                        ),
                      ),
                    ),
                    // Laid out but hidden and untappable on the placeholder, so the title row keeps
                    // the 48px the real button gives it.
                    Visibility(
                      visible: !isPlaceholder,
                      maintainSize: true,
                      maintainAnimation: true,
                      maintainState: true,
                      child: IconButton(
                        key: const Key('dismiss-job-result'),
                        icon: const Icon(Icons.close),
                        tooltip: 'Dismiss',
                        onPressed: onDismiss,
                      ),
                    ),
                  ],
                ),
                // The speedlines sit in what used to be a plain 8px gap, so firing them costs no
                // height. Drawn on a success only, from the middle outward.
                if (isPlaceholder || !succeeded || drama == JobDrama.clean)
                  const SizedBox(height: 8)
                else
                  ClipRect(
                    clipper: _FromCenterClipper(stampT),
                    child: SpeedlineDivider(
                      key: const Key('job-result-speedlines'),
                      color: scheme.onSurface,
                      height: 8,
                      opacity: 0.5,
                    ),
                  ),
                Visibility(
                  visible: !isPlaceholder,
                  maintainSize: true,
                  maintainAnimation: true,
                  maintainState: true,
                  child: Wrap(
                    spacing: 16,
                    runSpacing: 4,
                    children: [
                      for (var i = 0; i < tiles.length; i++)
                        _Stat(spec: tiles[i], progress: tileProgress(i)),
                    ],
                  ),
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
                  height: MediaQuery.textScalerOf(context).scale(JobResultPanel._noteFontSize) *
                      JobResultPanel._noteLineHeight *
                      2,
                  child: Text(
                    note,
                    key: const Key('job-result-note'),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.bodySmall?.copyWith(
                      fontSize: JobResultPanel._noteFontSize,
                      height: JobResultPanel._noteLineHeight,
                    ),
                  ),
                ),
                const SizedBox(height: 8),
                // `maintainSize` keeps the rank-up line's box reserved on the attempts that did not
                // rank up, so the celebratory line can appear without shoving the list.
                Visibility(
                  visible: result?.masteryRankedUp ?? false,
                  maintainSize: true,
                  maintainAnimation: true,
                  maintainState: true,
                  child: Opacity(
                    opacity: statsT,
                    child: Text(
                      'Mastery rank ${result?.mastery.rank ?? 0}!',
                      key: const Key('job-result-mastery'),
                      style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.bold),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );

    if (!anticipating || onSkip == null) return card;
    return GestureDetector(
      key: const Key('job-result-skip'),
      behavior: HitTestBehavior.opaque,
      onTap: onSkip,
      child: card,
    );
  }
}

/// A stat tile's content, resolved from the result before any animation touches it.
class _StatSpec {
  const _StatSpec({
    this.key,
    required this.label,
    this.prefix = '',
    this.amount,
    this.text,
    this.emphasised = false,
  });

  final Key? key;
  final String label;
  final String prefix;

  /// A number that counts up from zero. Null for a tile that only has [text].
  final int? amount;
  final String? text;

  /// Bolds the value, for the one tile that reports a loss rather than a gain.
  final bool emphasised;

  String get finalValue => text ?? '$prefix$amount';
}

class _Stat extends StatelessWidget {
  _Stat({required this.spec, required this.progress}) : super(key: spec.key);

  final _StatSpec spec;

  /// 0..1: fades the tile in and, for a number, counts it up.
  final double progress;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final base = theme.textTheme.titleMedium;
    final valueStyle = (spec.emphasised ? base?.copyWith(fontWeight: FontWeight.w800) : base)
        ?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]);

    final amount = spec.amount;
    final shown = amount == null
        ? spec.finalValue
        : '${spec.prefix}${(amount * Curves.easeOut.transform(progress)).round()}';

    // A counting number is narrower at 8 than at 250, and the tiles sit in a Wrap — so without a
    // reserved width every digit gained would nudge its neighbours sideways mid-reveal.
    final painter = TextPainter(
      text: TextSpan(text: spec.finalValue, style: valueStyle),
      textDirection: TextDirection.ltr,
      textScaler: MediaQuery.textScalerOf(context),
    )..layout();
    final finalWidth = painter.width;
    painter.dispose();

    return Semantics(
      label: '${spec.label} ${spec.finalValue}',
      excludeSemantics: true,
      child: Opacity(
        opacity: progress,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(spec.label, style: theme.textTheme.bodySmall),
            ConstrainedBox(
              constraints: BoxConstraints(minWidth: finalWidth),
              child: Text(shown, style: valueStyle),
            ),
          ],
        ),
      ),
    );
  }
}

/// Clips to the middle [fraction] of the width, so a child appears to fire outward from the centre.
class _FromCenterClipper extends CustomClipper<Rect> {
  const _FromCenterClipper(this.fraction);

  final double fraction;

  @override
  Rect getClip(Size size) {
    final width = size.width * fraction;
    return Rect.fromLTWH((size.width - width) / 2, 0, width, size.height);
  }

  @override
  bool shouldReclip(covariant _FromCenterClipper oldClipper) => oldClipper.fraction != fraction;
}
