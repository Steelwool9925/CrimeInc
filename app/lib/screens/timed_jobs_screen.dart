import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../theme/pack_theme.dart';
import '../widgets/meter_card.dart' show formatCountdown;

/// Overnight timed jobs ("appointments"): start one before putting the phone down, come back once
/// it finishes to collect. One slot at Tier 1.
///
/// Exactly two states, never both: **idle** (nothing running — every catalog entry startable,
/// subject to its own level/energy gate) and **occupied** (one running — shown live above the
/// catalog, which stays visible but goes inert). The catalog never disappears: hiding it while one
/// runs would make a player wonder what else is on offer; dimming it answers that without implying
/// it's startable.
class TimedJobsScreen extends StatefulWidget {
  const TimedJobsScreen({
    super.key,
    required this.appState,
    this.refreshInterval = const Duration(seconds: 30),
  });

  final AppState appState;

  /// How often the catalog and running row are re-read while the screen is open. Keeps the
  /// "ready" state honest against the server even if the local countdown's estimate drifts.
  final Duration refreshInterval;

  @override
  State<TimedJobsScreen> createState() => _TimedJobsScreenState();
}

class _TimedJobsScreenState extends State<TimedJobsScreen> {
  Timer? _refresher;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => widget.appState.loadTimedJobs());

    _refresher = Timer.periodic(widget.refreshInterval, (_) {
      // Never mid-action: the action's own response is newer than anything a read would return.
      if (!widget.appState.timedJobActionInFlight) widget.appState.loadTimedJobs();
    });
  }

  @override
  void dispose() {
    _refresher?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) => Scaffold(
        appBar: AppBar(title: const Text('Overnight Jobs')),
        body: _body(context, widget.appState),
      ),
    );
  }

  Widget _body(BuildContext context, AppState state) {
    if (!state.timedJobsLoaded) {
      if (state.timedJobsError != null) {
        return Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                state.timedJobsError!,
                key: const Key('timed-jobs-error'),
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 16),
              ElevatedButton(
                key: const Key('timed-jobs-retry'),
                onPressed: state.loadTimedJobs,
                child: const Text('Retry'),
              ),
            ],
          ),
        );
      }
      return const Center(child: CircularProgressIndicator());
    }

    final cashLabel = state.contentPack?.cashLabel ?? 'Cash';
    final message = state.actionMessageFor(ActionScope.timedJobs);
    final running = state.runningTimedJob;

    return Column(
      children: [
        if (message != null)
          MaterialBanner(
            key: const Key('action-message'),
            content: Text(message),
            actions: [
              TextButton(onPressed: state.clearActionMessage, child: const Text('Dismiss')),
            ],
          ),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              if (running != null) ...[
                _RunningJobCard(
                  job: running,
                  busy: state.timedJobCollectInFlight,
                  onCollect: state.collectTimedJob,
                ),
                const SizedBox(height: 8),
                // Reachable before the first dimmed card: the "why" arrives before the "what".
                Text(
                  'One at a time — collect this before starting another.',
                  key: const Key('timed-jobs-locked-note'),
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 8),
              ],
              for (final job in state.timedJobs)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: _CatalogCard(
                    job: job,
                    cashLabel: cashLabel,
                    busy: state.timedJobStartInFlight == job.id,
                    anyBusy: state.timedJobActionInFlight,
                    onStart: () => state.startTimedJob(job.id),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}

/// '8h', '24h', or e.g. '1h 30m' for a duration that is not a whole number of hours.
String timedJobDurationLabel(int seconds) {
  final hours = seconds ~/ 3600;
  final minutes = (seconds % 3600) ~/ 60;
  if (minutes == 0) return '${hours}h';
  return '${hours}h ${minutes}m';
}

/// 'Energy 3 · Cash 180-320 · 40 XP', the same joined-and-dropped-if-zero style
/// [objectiveRewardLabel] uses.
String timedJobDetailsLabel(TimedJob job, String cashLabel) {
  final cashRange = job.cashMin == job.cashMax
      ? '$cashLabel ${job.cashMin}'
      : '$cashLabel ${job.cashMin}-${job.cashMax}';
  final parts = [
    'Energy ${job.energyCost}',
    cashRange,
    if (job.xp > 0) '${job.xp} XP',
  ];
  return parts.join(' · ');
}

/// The live countdown for the one running appointment.
///
/// Mirrors [MeterCard]'s tick discipline exactly: sync `_remaining`/`_ready` from the server on
/// arrival, tick locally every second, re-sync whenever a fresh [RunningTimedJob] arrives, hold
/// at ready rather than go negative. A deliberate copy, not a shared base class — the two widgets'
/// shapes differ enough (a bar with a max vs. a single countdown with none, and this one's subject
/// can disappear entirely on collect) that forcing an abstraction now would be premature.
class _RunningJobCard extends StatefulWidget {
  const _RunningJobCard({
    required this.job,
    required this.busy,
    required this.onCollect,
    this.tickInterval = const Duration(seconds: 1),
  });

  final RunningTimedJob job;
  final bool busy;
  final VoidCallback onCollect;

  /// Injectable so widget tests can drive the countdown deterministically.
  final Duration tickInterval;

  @override
  State<_RunningJobCard> createState() => _RunningJobCardState();
}

class _RunningJobCardState extends State<_RunningJobCard> {
  Timer? _ticker;
  int _remaining = 0;
  bool _ready = false;

  @override
  void initState() {
    super.initState();
    _syncFromServer();
  }

  @override
  void didUpdateWidget(_RunningJobCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Re-sync on any fresh figure from the server, same reasoning as MeterCard: identity, not
    // value, is what changed, and a fresh read is always newer than the local estimate.
    if (oldWidget.job != widget.job) _syncFromServer();
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  void _syncFromServer() {
    _remaining = widget.job.remainingSeconds;
    _ready = widget.job.ready;
    _restartTicker();
  }

  void _restartTicker() {
    _ticker?.cancel();
    _ticker = null;

    if (_ready || _remaining <= 0) return;

    _ticker = Timer.periodic(widget.tickInterval, (_) {
      if (!mounted) return;
      final next = _remaining - 1;
      if (next <= 0) {
        // The duration has genuinely elapsed on the local clock — collect becomes available
        // immediately rather than waiting on the next poll. The server re-verifies on tap
        // regardless, so an optimistic flip here is safe.
        setState(() {
          _remaining = 0;
          _ready = true;
        });
        _ticker?.cancel();
        _ticker = null;
        return;
      }
      setState(() => _remaining = next);
    });
  }

  @override
  Widget build(BuildContext context) {
    final job = widget.job;

    return Card(
      key: const Key('timed-job-running'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(job.name, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            // One announcement, not a bare number with no referent — same reasoning as
            // MeterCard._semanticSummary.
            Semantics(
              label: _ready
                  ? '${job.name}, ready to collect'
                  : '${job.name}, ready in ${formatCountdown(_remaining)}',
              excludeSemantics: true,
              child: Text(
                _ready ? 'Ready' : formatCountdown(_remaining),
                style: statNumberStyle(context, fontSize: 18),
              ),
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: FilledButton(
                key: const Key('collect-timed-job'),
                onPressed: _ready && !widget.busy ? widget.onCollect : null,
                // The label itself carries "not ready yet" rather than just greying out with no
                // text — a disabled button with no explanation reads as broken, not "wait".
                child: widget.busy
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : Text(_ready ? 'Collect' : 'Not ready yet'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CatalogCard extends StatelessWidget {
  const _CatalogCard({
    required this.job,
    required this.cashLabel,
    required this.busy,
    required this.anyBusy,
    required this.onStart,
  });

  final TimedJob job;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onStart;

  String get _lockLabel => switch (job.unavailableReason) {
        'alreadyRunning' => 'Running one already',
        'level' => 'Level ${job.levelRequirement} required',
        'energy' => 'Not enough energy',
        _ => 'Unavailable',
      };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final locked = !job.canStart;

    // "Present but not actionable gets a dimmer outline, not hidden" — the same rule
    // _DashboardTile.comingSoon follows on Home.
    final dimBorder = theme.colorScheme.onSurface.withValues(alpha: 0.35);
    final dimContent = theme.colorScheme.onSurface.withValues(alpha: 0.5);

    return Card(
      key: Key('timed-job-card-${job.id}'),
      shape: locked
          ? (theme.cardTheme.shape as RoundedRectangleBorder?)?.copyWith(
              side: BorderSide(color: dimBorder, width: 2.5),
            )
          : null,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    job.name,
                    style: theme.textTheme.titleMedium
                        ?.copyWith(color: locked ? dimContent : null),
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  timedJobDurationLabel(job.durationSeconds),
                  style: statNumberStyle(context, fontSize: 14)
                      .copyWith(color: locked ? dimContent : null),
                ),
              ],
            ),
            if (job.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(
                job.description,
                style: theme.textTheme.bodySmall?.copyWith(color: locked ? dimContent : null),
              ),
            ],
            const SizedBox(height: 8),
            Text(
              timedJobDetailsLabel(job, cashLabel),
              key: Key('timed-job-details-${job.id}'),
              style: theme.textTheme.bodySmall?.copyWith(
                color: locked ? dimContent : theme.colorScheme.onSurface,
              ),
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: locked
                  ? OutlinedButton(
                      key: Key('start-timed-job-${job.id}'),
                      onPressed: null,
                      child: Text(_lockLabel),
                    )
                  : FilledButton(
                      key: Key('start-timed-job-${job.id}'),
                      onPressed: anyBusy ? null : onStart,
                      child: busy
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(strokeWidth: 2))
                          : const Text('Start'),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}
