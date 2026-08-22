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
    );
  }

  Widget _status(ThemeData theme) {
    if (_isFull) {
      return Text(
        'Full',
        style: theme.textTheme.bodySmall?.copyWith(
          color: theme.colorScheme.primary,
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
