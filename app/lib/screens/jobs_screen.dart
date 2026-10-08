import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../widgets/job_drama.dart';
import '../widgets/job_result_panel.dart';
import '../widgets/level_up_dialog.dart';

/// The job list — the core of the game loop.
///
/// Every gating decision shown here comes from the server (`canAttempt`, `unavailableReason`); the
/// client only renders it. Re-deriving affordability locally would risk showing a job as available
/// that the server then refuses — which is exactly why this screen re-reads on a timer (below)
/// rather than trying to guess gating locally between reads.
class JobsScreen extends StatefulWidget {
  const JobsScreen({
    super.key,
    required this.appState,
    this.refreshInterval = const Duration(seconds: 10),
  });

  final AppState appState;

  /// How often the catalog is re-read while this screen is open.
  ///
  /// Energy regenerates and gear-driven stats change on their own, with nothing that visits this
  /// screen to say so — the same reason `PropertiesScreen` re-reads on a timer. Mirrors that
  /// screen's cadence and in-flight guard exactly (see `_refresher` below).
  final Duration refreshInterval;

  @override
  State<JobsScreen> createState() => _JobsScreenState();
}

class _JobsScreenState extends State<JobsScreen> {
  Timer? _refresher;

  /// Whether a level-up dialog is already on screen. See [_maybeShowLevelUp].
  bool _levelUpShowing = false;

  /// The result whose reveal has finished playing. A level-up waits for this to match the result on
  /// show, so the dialog arrives after the payout lands instead of covering it.
  JobResult? _revealedResult;

  /// How many extra copies of each item the player has dialled in, per job: `jobId -> itemId -> n`.
  /// Held here rather than in each card so it survives the list rebuilding on every refresh, and
  /// cleared when the attempt is made so the next one starts from none — extras are spent, so a
  /// leftover selection would quietly spend items on an attempt the player did not mean to pad.
  final Map<String, Map<String, int>> _extras = {};

  /// The extras for [job] that are still affordable right now. Re-clamped on every read because
  /// stock changes under a stored selection (a refresh, an equip, another attempt).
  Map<String, int> _chosenExtras(Job job) => {
        for (final r in job.itemRequirements)
          if (r.maxExtras > 0)
            if ((_extras[job.id]?[r.itemId] ?? 0).clamp(0, r.availableExtras) case final n when n > 0)
              r.itemId: n,
      };

  @override
  void initState() {
    super.initState();
    // Load after the first frame so the screen can show its loading state.
    WidgetsBinding.instance.addPostFrameCallback((_) => widget.appState.loadJobs());

    _refresher = Timer.periodic(widget.refreshInterval, (_) {
      // Never while an action is mid-flight: that action holds its own guard across its own
      // outcome handling, and a poll landing inside it would be collapsed into that read and lost —
      // the same reasoning `PropertiesScreen`'s `_refresher` guards on `propertyInFlight` for.
      if (widget.appState.jobInFlight == null) {
        widget.appState.loadJobs();
      }
    });
  }

  @override
  void dispose() {
    _refresher?.cancel();
    // A result describes the attempt just made. Coming back to Jobs later must not greet the player
    // with an old "Job complete"; the panel is then the empty placeholder.
    widget.appState.forgetJobResult();
    super.dispose();
  }

  /// Shows the level-up dialog when one is pending, then clears it.
  ///
  /// `_levelUpShowing` is load-bearing, and this is why: this method is called from [build], which
  /// re-runs on every `notifyListeners` — including the ten-second `_refresher` tick. Without the
  /// guard, each of those rebuilds saw `pendingLevelUp` still set and pushed *another* identical
  /// dialog on top of the one already open. A player who took longer than one tick to dismiss it
  /// ended up behind a stack of copies, and tapping "Nice" popped only the top one and revealed
  /// the next — which reads as a button that does nothing. (Barrier taps behaved the same way, so
  /// the dialog also looked non-dismissible.)
  ///
  /// The flag is set synchronously here rather than inside the post-frame callback: two builds can
  /// be scheduled before either callback runs, and both would otherwise pass the check.
  void _maybeShowLevelUp(BuildContext context, AppState state) {
    final levelUp = state.pendingLevelUp;
    if (levelUp == null || _levelUpShowing) return;

    // The level-up is the crescendo of the reveal, not a competitor to it. With no result on show
    // (it was dismissed) there is nothing to wait for.
    final result = state.lastJobResult;
    if (result != null && !identical(result, _revealedResult)) return;

    _levelUpShowing = true;

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!context.mounted || state.pendingLevelUp == null) {
        _levelUpShowing = false;
        return;
      }

      showDialog<void>(
        context: context,
        builder: (dialogContext) => LevelUpDialog(
          levelUp: levelUp,
          onAcknowledge: () {
            Navigator.of(dialogContext).pop();
            state.acknowledgeLevelUp();
          },
        ),
      ).whenComplete(() {
        _levelUpShowing = false;
        // Escape and barrier taps pop the route without running `onAcknowledge`, which would leave
        // `pendingLevelUp` set and re-open the dialog on the very next rebuild. Treat any dismissal
        // as acknowledgement; the call is idempotent.
        state.acknowledgeLevelUp();
      });
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
              style: TextStyle(color: Theme.of(context).colorScheme.error),
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
        // Always mounted, with a same-sized placeholder before the first result and after a
        // dismissal: the list sits beneath it, so a panel that appeared from nothing pushed every
        // "Do job" button down by its own height on the first attempt of a session.
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: JobResultPanel(
            result: state.lastJobResult,
            cashLabel: cashLabel,
            onDismiss: state.dismissJobResult,
            drama: state.lastJobDrama ?? JobDrama.clean,
            anticipating: state.anticipating,
            onSkip: state.skipReveal,
            onRevealed: (result) {
              _revealedResult = result;
              // Only a waiting level-up needs the screen rebuilt; otherwise the end of a reveal
              // would redraw every job card for nothing.
              if (state.pendingLevelUp != null) setState(() {});
            },
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
                  chosenExtras: _chosenExtras(job),
                  onExtrasChanged: (itemId, count) =>
                      setState(() => (_extras[job.id] ??= {})[itemId] = count),
                  onDo: () {
                    final extras = _chosenExtras(job);
                    setState(() => _extras.remove(job.id));
                    state.doJob(job.id, extras: extras);
                  },
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
    required this.chosenExtras,
    required this.onExtrasChanged,
    required this.onDo,
  });

  final Job job;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;

  /// Extras dialled in for this job right now, `itemId -> n`, already clamped to what is affordable.
  final Map<String, int> chosenExtras;

  final void Function(String itemId, int count) onExtrasChanged;
  final VoidCallback onDo;

  /// Wording for the server's refusal token. The server sends a stable token, not display text, so
  /// the phrasing lives here.
  String get _lockLabel => switch (job.unavailableReason) {
        'level' => 'Requires level ${job.levelRequirement}',
        'stats' =>
          'Needs ${job.attackRequirement} attack / ${job.defenseRequirement} defense',
        'items' => 'Needs $_unmetItemRequirementsLabel',
        'energy' => 'Needs ${job.energyCost} energy',
        _ => 'Unavailable',
      };

  /// Describes the item requirements this player has not yet met, e.g. "2 spare switchblade" — or
  /// every unmet one joined together, for a job with more than one. "Spare" because the copy a
  /// player is wearing is never the one a job spends: someone wearing their only switchblade is
  /// still short one, and a bare "Needs 1 switchblade" beside a Market card saying "Owned: 1" reads
  /// as a bug. Uses the raw content id since the server does not send a display name here.
  String get _unmetItemRequirementsLabel => job.itemRequirements
      .where((r) => !r.met)
      .map((r) => countedItem(r.minQuantity, r.itemId).replaceFirst(' ', ' spare '))
      .join(', ');

  /// What an attempt uses up, stated before the player commits to it — shown on the card whether or
  /// not the job is currently available, so the cost is known before the button is ever enabled.
  String get _usesLabel =>
      'Uses up ${job.itemRequirements.map((r) => countedItem(r.minQuantity, r.itemId)).join(', ')} '
      'each attempt (never the one you are wearing)';

  /// Each extra's worth as a whole percentage, e.g. "10%". Rounded for display only.
  String get _extraChanceLabel => '${(job.extraSuccessChance * 100).round()}%';

  /// How many extras are dialled in across every requirement.
  int get _extrasTotal => chosenExtras.values.fold(0, (a, b) => a + b);

  /// The success chance as a whole percentage, previewed for the extras dialled in right now.
  ///
  /// Rounded for display only. With no extras it is exactly the figure the server sends; with some
  /// it is [Job.chanceWithExtras], the server's own shape (extras add, the ceiling caps), so the
  /// player sees what the extras buy before paying for them. The server still rolls the real odds.
  String get _chanceLabel => '${(job.chanceWithExtras(_extrasTotal) * 100).round()}%';

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
            if (job.itemRequirements.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(
                _usesLabel,
                key: Key('job-uses-${job.id}'),
                style: theme.textTheme.bodySmall,
              ),
            ],
            // Extras are only offered when the job can actually be done and there is something to add:
            // a stepper on a locked card would invite choosing a cost for an attempt that cannot happen.
            // The row stays while the job can be done and extras exist at all, even with none to
            // spare right now (both buttons disabled). Dropping it at the last spare shrank the card
            // and moved "Do job" up from under the cursor between two attempts.
            if (job.canAttempt)
              for (final r in job.itemRequirements.where((r) => r.maxExtras > 0)) ...[
                const SizedBox(height: 4),
                _ExtrasStepper(
                  jobId: job.id,
                  requirement: r,
                  count: chosenExtras[r.itemId] ?? 0,
                  eachLabel: _extraChanceLabel,
                  enabled: !anyBusy,
                  onChanged: (n) => onExtrasChanged(r.itemId, n),
                ),
              ],
            if (job.mastery.maxRank > 0) ...[
              const SizedBox(height: 8),
              // The bar and its "Rank x/y" caption are one fact, announced as one.
              //
              // An unlabelled LinearProgressIndicator has no accessible name of its own, so the
              // engine reaches outward and absorbs whatever text is nearby — this one announced
              // itself as a progress bar named after the entire job card, title and description
              // and all, while the rank it actually represents went unsaid. Naming it explicitly
              // and excluding the caption it duplicates replaces that with the one short phrase
              // a sighted player reads off it.
              Semantics(
                label: 'Mastery rank ${job.mastery.rank} of ${job.mastery.maxRank}',
                excludeSemantics: true,
                child: Row(
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
                      // Visually just "Do job" — the card above it supplies the context a sighted
                      // player has at a glance. Non-visually that context is gone, and every one
                      // of these buttons announced the identical name, so the job list read as six
                      // indistinguishable "Do job" buttons and the core loop could not be operated
                      // at all. The accessible name carries the job, and the reason when the
                      // button is refused, since that reason otherwise sits in a separate node
                      // the reader may never associate with it.
                      : Text(
                          'Do job',
                          semanticsLabel: job.canAttempt
                              ? 'Do job: ${job.name}'
                              : 'Do job: ${job.name}. Unavailable: $_lockLabel',
                        ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// "Add extra switchblade  - 1 +  (+10% success each)" — how many items beyond the requirement the
/// player is willing to spend for better odds.
///
/// Every extra is spent whether the attempt wins or loses, which the line says before the player
/// commits rather than after. The count is bounded by what the content allows and by the spare copies
/// the player holds; the server checks both again and works out the resulting odds, so this only ever
/// proposes a number.
class _ExtrasStepper extends StatelessWidget {
  const _ExtrasStepper({
    required this.jobId,
    required this.requirement,
    required this.count,
    required this.eachLabel,
    required this.enabled,
    required this.onChanged,
  });

  final String jobId;
  final JobItemRequirement requirement;
  final int count;
  final String eachLabel;
  final bool enabled;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final id = requirement.itemId;
    final max = requirement.availableExtras;

    return Row(
      children: [
        Expanded(
          child: Text(
            max > 0
                ? 'Add extra $id  (+$eachLabel success each, spent win or lose)'
                : 'Add extra $id  (no spare copies to add)',
            key: Key('job-extras-label-$jobId-$id'),
            style: theme.textTheme.bodySmall,
          ),
        ),
        IconButton(
          key: Key('job-extras-less-$jobId-$id'),
          icon: const Icon(Icons.remove),
          tooltip: 'One fewer extra $id',
          onPressed: enabled && count > 0 ? () => onChanged(count - 1) : null,
        ),
        Semantics(
          label: '$count extra $id, up to $max',
          excludeSemantics: true,
          child: SizedBox(
            width: 24,
            child: Text(
              '$count',
              key: Key('job-extras-count-$jobId-$id'),
              textAlign: TextAlign.center,
              style: theme.textTheme.titleMedium,
            ),
          ),
        ),
        IconButton(
          key: Key('job-extras-more-$jobId-$id'),
          icon: const Icon(Icons.add),
          tooltip: 'One more extra $id',
          onPressed: enabled && count < max ? () => onChanged(count + 1) : null,
        ),
      ],
    );
  }
}
