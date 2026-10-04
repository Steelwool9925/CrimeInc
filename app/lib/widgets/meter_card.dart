import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';

/// A resource meter with a regen countdown that actually moves (T4.2).
///
/// The server reports `secondsUntilNextPoint` and [AppState] polls every ten seconds. Rendering
/// that figure directly meant the countdown sat still and then jumped ten, which reads as a
/// frozen UI. This ticks locally between polls and re-syncs whenever a fresh figure arrives:
/// the local count is an estimate, the server is authoritative the moment it speaks.

/// Formats a countdown for display: `45s`, `1m 05s`, or `now` at or past zero.
///
/// Seconds are zero-padded above a minute so the label keeps a stable width and does not
/// jitter as it counts down.
String formatCountdown(int seconds) {
  if (seconds <= 0) return 'now';
  if (seconds < 60) return '${seconds}s';

  final minutes = seconds ~/ 60;
  final remainder = seconds % 60;
  return '${minutes}m ${remainder.toString().padLeft(2, '0')}s';
}

class MeterCard extends StatefulWidget {
  const MeterCard({
    super.key,
    required this.label,
    required this.meter,
    this.tickInterval = const Duration(seconds: 1),
  });

  final String label;
  final MeterState meter;

  /// Injectable so widget tests can drive the countdown deterministically, matching the
  /// convention PropertiesScreen already uses for its accrual ticker.
  final Duration tickInterval;

  @override
  State<MeterCard> createState() => _MeterCardState();
}

class _MeterCardState extends State<MeterCard> {
  Timer? _ticker;
  int? _remaining;

  bool get _isFull => widget.meter.current >= widget.meter.max;

  @override
  void initState() {
    super.initState();
    _syncFromServer();
  }

  @override
  void didUpdateWidget(MeterCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Re-sync on any fresh figure from the server, including the case where a poll returns the
    // same number — that means a point landed and the cycle restarted, not that nothing moved.
    if (oldWidget.meter != widget.meter) {
      _syncFromServer();
    }
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  void _syncFromServer() {
    _remaining = widget.meter.secondsUntilNextPoint;
    _restartTicker();
  }

  void _restartTicker() {
    _ticker?.cancel();
    _ticker = null;

    // Nothing to count toward on a full meter, or when the server sent no timing at all.
    if (_isFull || _remaining == null) return;

    _ticker = Timer.periodic(widget.tickInterval, (_) {
      if (!mounted) return;
      final next = (_remaining ?? 0) - 1;
      if (next <= 0) {
        // Hold at zero and stop; the next poll supplies the new cycle. Counting into negatives
        // would only advertise how stale the estimate is.
        setState(() => _remaining = 0);
        _ticker?.cancel();
        _ticker = null;
        return;
      }
      setState(() => _remaining = next);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final meter = widget.meter;
    final progress = meter.max > 0 ? meter.current / meter.max : 0.0;

    return Card(
      // One announcement per meter, not four.
      //
      // Visually this is a label, a count, a bar and a status line, and the eye fuses them
      // instantly. To a screen reader they were four unrelated nodes — "Energy", "20 / 20", a
      // progress bar, "Full" — read in sequence with nothing tying them together, and the bar
      // announcing a bare percentage of its own. `MergeSemantics` plus an explicit label on the
      // whole card collapses that into the sentence a sighted player actually perceives.
      child: MergeSemantics(
        child: Semantics(
          label: _semanticSummary(),
          excludeSemantics: true,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(widget.label, style: theme.textTheme.titleMedium),
                    Text('${meter.current} / ${meter.max}',
                        style: theme.textTheme.bodyMedium),
                  ],
                ),
                const SizedBox(height: 8),
                LinearProgressIndicator(value: progress.clamp(0.0, 1.0)),
                const SizedBox(height: 4),
                _status(theme),
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// The whole card as one sentence, e.g. "Energy, 20 of 20, full" or
  /// "Energy, 7 of 20, next point in 1m 30s".
  ///
  /// Spelled out rather than assembled from the visible glyphs: "20 / 20" is read as a date or a
  /// fraction depending on the reader, and the status line is a fragment on its own.
  String _semanticSummary() {
    final meter = widget.meter;
    final buffer = StringBuffer('${widget.label}, ${meter.current} of ${meter.max}');

    if (_isFull) {
      buffer.write(', full');
    } else {
      final remaining = _remaining;
      if (remaining != null) {
        buffer.write(remaining <= 0
            ? ', next point due now'
            : ', next point in ${formatCountdown(remaining)}');
      }
    }

    return buffer.toString();
  }

  Widget _status(ThemeData theme) {
    if (_isFull) {
      // Deliberately onSurface, not colorScheme.primary: primary is the theme's one spot
      // colour (danger/CTA/selected-nav) and a full meter is good news, not a warning.
      return Text(
        'Full',
        style: theme.textTheme.bodySmall?.copyWith(
          color: theme.colorScheme.onSurface,
          fontWeight: FontWeight.w600,
        ),
      );
    }

    final remaining = _remaining;
    if (remaining == null) {
      // The server sent no timing for this meter; say nothing rather than guess.
      return const SizedBox(height: 0);
    }

    return Text(
      remaining <= 0 ? '+1 due now' : '+1 in ${formatCountdown(remaining)}',
      style: theme.textTheme.bodySmall,
    );
  }
}
