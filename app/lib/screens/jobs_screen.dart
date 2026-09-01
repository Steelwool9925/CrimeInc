import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/job_result_panel.dart';
import '../widgets/level_up_dialog.dart';

/// The job list — the core of the game loop.
///
/// Every gating decision shown here comes from the server (`canAttempt`, `unavailableReason`); the
/// client only renders it. Re-deriving affordability locally would risk showing a job as available
/// that the server then refuses.
class JobsScreen extends StatefulWidget {
  const JobsScreen({super.key, required this.appState});

  final AppState appState;

  @override
  State<JobsScreen> createState() => _JobsScreenState();
}

class _JobsScreenState extends State<JobsScreen> {
  @override
  void initState() {
    super.initState();
    // Load after the first frame so the screen can show its loading state.
    WidgetsBinding.instance.addPostFrameCallback((_) => widget.appState.loadJobs());
  }

  /// Shows the level-up dialog when one is pending, then clears it.
  void _maybeShowLevelUp(BuildContext context, AppState state) {
    final levelUp = state.pendingLevelUp;
    if (levelUp == null) return;

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!context.mounted || state.pendingLevelUp == null) return;

      showDialog<void>(
        context: context,
        builder: (dialogContext) => LevelUpDialog(
          levelUp: levelUp,
          onAcknowledge: () {
            Navigator.of(dialogContext).pop();
            state.acknowledgeLevelUp();
          },
        ),
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) {
        final state = widget.appState;
        _maybeShowLevelUp(context, state);

        return Scaffold(
          appBar: AppBar(
            title: const Text('Jobs'),
            actions: [
              if (state.me != null)
                Padding(
                  padding: const EdgeInsets.only(right: 16),
                  child: Center(
                    child: Text(
                      'Energy ${state.me!.energy.current}/${state.me!.energy.max}',
                      key: const Key('jobs-energy'),
                    ),
                  ),
                ),
            ],
          ),
          body: _buildBody(context, state),
        );
      },
    );
  }

  Widget _buildBody(BuildContext context, AppState state) {
    if (state.jobsLoading && state.jobs.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.jobsError != null && state.jobs.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              state.jobsError!,
              key: const Key('jobs-error'),
              textAlign: TextAlign.center,
              style: const TextStyle(color: Colors.red),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              key: const Key('jobs-retry'),
              onPressed: state.loadJobs,
              child: const Text('Retry'),
            ),
          ],
        ),
      );
    }

    final cashLabel = state.contentPack?.cashLabel ?? 'Cash';

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
        if (state.lastJobResult != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
            child: JobResultPanel(
              result: state.lastJobResult!,
              cashLabel: cashLabel,
              onDismiss: state.dismissJobResult,
            ),
          ),
        Expanded(
          child: ListView.builder(
            padding: const EdgeInsets.all(16),
            itemCount: state.jobs.length,
            itemBuilder: (context, index) {
              final job = state.jobs[index];
              return Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: _JobCard(
                  job: job,
                  cashLabel: cashLabel,
                  busy: state.jobInFlight == job.id,
                  anyBusy: state.jobInFlight != null,
                  onDo: () => state.doJob(job.id),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _JobCard extends StatelessWidget {
  const _JobCard({
    required this.job,
    required this.cashLabel,
    required this.busy,
    required this.anyBusy,
    required this.onDo,
  });

  final Job job;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onDo;

  /// Wording for the server's refusal token. The server sends a stable token, not display text, so
  /// the phrasing lives here.
  String get _lockLabel => switch (job.unavailableReason) {
        'level' => 'Requires level ${job.levelRequirement}',
        'stats' =>
          'Needs ${job.attackRequirement} attack / ${job.defenseRequirement} defense',
        'energy' => 'Needs ${job.energyCost} energy',
        _ => 'Unavailable',
      };

  /// The success chance as a whole percentage.
  ///
  /// Rounded for display only. The server sends the exact figure it will roll against, so this is
  /// the one place the number is approximated and it never feeds a decision.
  String get _chanceLabel => '${(job.successChance * 100).round()}%';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final enabled = job.canAttempt && !anyBusy;

    return Card(
      key: Key('job-card-${job.id}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(job.name, style: theme.textTheme.titleMedium),
                ),
                Text('${job.energyCost} energy', style: theme.textTheme.bodySmall),
              ],
            ),
            if (job.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(job.description, style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            Text(
              '$cashLabel ${job.cashMin}–${job.cashMax}  ·  ${job.xp} XP',
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 4),
            Text(
              '$_chanceLabel success  ·  needs ${job.attackRequirement} attack / '
              '${job.defenseRequirement} defense',
              key: Key('job-odds-${job.id}'),
              style: theme.textTheme.bodySmall,
            ),
            if (job.mastery.maxRank > 0) ...[
              const SizedBox(height: 8),
              Row(
                children: [
                  Expanded(
                    child: LinearProgressIndicator(
                      key: Key('mastery-bar-${job.id}'),
                      value: job.mastery.rankProgress,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    'Rank ${job.mastery.rank}/${job.mastery.maxRank}',
                    style: theme.textTheme.bodySmall,
                  ),
                ],
              ),
            ],
            const SizedBox(height: 8),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: !job.canAttempt
                      ? Text(
                          _lockLabel,
                          key: Key('job-lock-${job.id}'),
                          style: theme.textTheme.bodySmall
                              ?.copyWith(color: theme.colorScheme.error),
                        )
                      : const SizedBox.shrink(),
                ),
                ElevatedButton(
                  key: Key('do-job-${job.id}'),
                  onPressed: enabled ? onDo : null,
                  child: busy
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Do job'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
