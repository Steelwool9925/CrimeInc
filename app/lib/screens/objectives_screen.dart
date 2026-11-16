import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../theme/pack_theme.dart';
import '../widgets/accessible_name.dart';
import '../widgets/level_up_dialog.dart';
import 'jobs_screen.dart';
import 'properties_screen.dart';

/// Daily objectives: today's set with a bar for each, a Claim button once one is finished, and a
/// one-tap way to the screen where an unfinished one is worked on.
///
/// Every figure is the server's. The set, the progress and whether a reward can be taken all arrive
/// from `GET /objectives` or from the response to the action that moved them; the client only draws
/// them. The one thing it works out is the "resets in" countdown, from the reset instant the server
/// sent.
class ObjectivesScreen extends StatefulWidget {
  const ObjectivesScreen({
    super.key,
    required this.appState,
    this.refreshInterval = const Duration(seconds: 30),
    this.clock = DateTime.now,
  });

  final AppState appState;

  /// How often the set is re-read while the screen is open. It keeps the countdown honest and picks
  /// up a new day without the player having to leave and return.
  final Duration refreshInterval;

  /// The current time, for the countdown. Injected so a test can pin it.
  final DateTime Function() clock;

  @override
  State<ObjectivesScreen> createState() => _ObjectivesScreenState();
}

class _ObjectivesScreenState extends State<ObjectivesScreen> {
  Timer? _refresher;

  /// Whether a level-up dialog is open or about to be. Set synchronously: build can run twice before
  /// the post-frame callback does, and both would otherwise open one. See `JobsScreen`.
  bool _levelUpShowing = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => widget.appState.loadObjectives());

    _refresher = Timer.periodic(widget.refreshInterval, (_) {
      // Never mid-claim: the claim's own response is newer than anything a read would return.
      if (widget.appState.objectiveInFlight == null) widget.appState.loadObjectives();
    });
  }

  @override
  void dispose() {
    _refresher?.cancel();
    super.dispose();
  }

  /// Claiming the reward can cross a level, and the dialog that announces it otherwise lives only on
  /// the Jobs screen. Shown here so the player sees it where they earned it.
  ///
  /// Only while this screen is the one in front. "Go to Jobs" pushes Jobs on top of this screen, which
  /// stays alive underneath and keeps rebuilding; without this check a level-up from a job done there
  /// would pop the dialog from here, over the job's reveal, before `JobsScreen` (which waits for the
  /// reveal to finish) got to show it.
  void _maybeShowLevelUp(BuildContext context, AppState state) {
    final levelUp = state.pendingLevelUp;
    if (levelUp == null || _levelUpShowing) return;
    if (!(ModalRoute.of(context)?.isCurrent ?? true)) return;

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
        // Escape and barrier taps pop without running onAcknowledge; treat any dismissal as one.
        state.acknowledgeLevelUp();
      });
    });
  }

  void _goTo(BuildContext context, DailyObjective objective) {
    final screen = switch (objective.destination) {
      ObjectiveDestination.jobs => JobsScreen(appState: widget.appState),
      ObjectiveDestination.properties => PropertiesScreen(appState: widget.appState),
    };
    Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen));
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) {
        final state = widget.appState;
        _maybeShowLevelUp(context, state);

        return Scaffold(
          appBar: AppBar(title: const Text('Daily objectives')),
          body: _body(context, state),
        );
      },
    );
  }

  Widget _body(BuildContext context, AppState state) {
    if (!state.objectivesLoaded) {
      if (state.objectivesError != null) {
        return Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                state.objectivesError!,
                key: const Key('objectives-error'),
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 16),
              ElevatedButton(
                key: const Key('objectives-retry'),
                onPressed: state.loadObjectives,
                child: const Text('Retry'),
              ),
            ],
          ),
        );
      }
      return const Center(child: CircularProgressIndicator());
    }

    final cashLabel = state.contentPack?.cashLabel ?? 'Cash';
    final message = state.actionMessageFor(ActionScope.objectives);

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
          child: state.objectives.isEmpty
              ? const Center(
                  child: Padding(
                    padding: EdgeInsets.all(24),
                    child: Text(
                      'Nothing on offer today. Check back after the reset.',
                      key: Key('objectives-empty'),
                      textAlign: TextAlign.center,
                    ),
                  ),
                )
              : ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    for (final objective in state.objectives)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: _ObjectiveCard(
                          objective: objective,
                          cashLabel: cashLabel,
                          busy: state.objectiveInFlight == objective.id,
                          anyBusy: state.objectiveInFlight != null,
                          onClaim: () => state.claimObjective(objective.id),
                          onGo: () => _goTo(context, objective),
                        ),
                      ),
                    if (state.objectivesResetsAt case final resets?)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          resetsInLabel(resets, widget.clock()),
                          key: const Key('objectives-reset'),
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ),
                  ],
                ),
        ),
      ],
    );
  }
}

/// "Resets in 5h 12m", "Resets in 12m", or "Resetting now" at or past the instant.
///
/// Whole minutes, rounded up, so the label never claims a reset is closer than it is.
String resetsInLabel(DateTime resetsAtUtc, DateTime now) {
  final remaining = resetsAtUtc.difference(now.toUtc());
  if (remaining <= Duration.zero) return 'Resetting now';

  final minutes = (remaining.inSeconds / 60).ceil();
  final hours = minutes ~/ 60;
  final rest = minutes % 60;
  if (hours == 0) return 'Resets in ${rest}m';
  return rest == 0 ? 'Resets in ${hours}h' : 'Resets in ${hours}h ${rest}m';
}

/// "$110 · 25 XP", leaving out whichever half is zero.
String objectiveRewardLabel(DailyObjective o, String cashLabel) {
  final parts = [
    if (o.rewardCash > 0) '$cashLabel ${o.rewardCash}',
    if (o.rewardXp > 0) '${o.rewardXp} XP',
  ];
  return parts.join(' · ');
}

class _ObjectiveCard extends StatelessWidget {
  const _ObjectiveCard({
    required this.objective,
    required this.cashLabel,
    required this.busy,
    required this.anyBusy,
    required this.onClaim,
    required this.onGo,
  });

  final DailyObjective objective;
  final String cashLabel;
  final bool busy;
  final bool anyBusy;
  final VoidCallback onClaim;
  final VoidCallback onGo;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final o = objective;
    final reward = objectiveRewardLabel(o, cashLabel);
    final destination = o.destination == ObjectiveDestination.jobs ? 'Jobs' : 'Properties';

    return Card(
      key: Key('objective-card-${o.id}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text(o.name, style: theme.textTheme.titleMedium)),
                const SizedBox(width: 8),
                Text(
                  reward,
                  key: Key('objective-reward-${o.id}'),
                  style: statNumberStyle(context, fontSize: 14),
                ),
              ],
            ),
            if (o.description.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(o.description, style: theme.textTheme.bodySmall),
            ],
            const SizedBox(height: 8),
            // The bar and its count are one fact, announced as one. An unlabelled progress bar has no
            // accessible name of its own and absorbs the text around it, taking the buttons with it.
            Semantics(
              label: '${o.name}: ${o.progress} of ${o.target}${o.claimed ? ', claimed' : ''}',
              excludeSemantics: true,
              child: Row(
                children: [
                  Expanded(
                    child: LinearProgressIndicator(
                      key: Key('objective-bar-${o.id}'),
                      value: o.fraction,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    '${o.progress}/${o.target}',
                    key: Key('objective-count-${o.id}'),
                    style: theme.textTheme.bodySmall,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: _action(context, destination, reward),
            ),
          ],
        ),
      ),
    );
  }

  Widget _action(BuildContext context, String destination, String reward) {
    final o = objective;

    // Words as well as the icon: colour and shape are never the only carrier.
    if (o.claimed) {
      return Row(
        key: Key('objective-claimed-${o.id}'),
        mainAxisSize: MainAxisSize.min,
        children: const [
          Icon(Icons.check_circle_outline, size: 18),
          SizedBox(width: 6),
          Text('Claimed'),
        ],
      );
    }

    if (o.complete) {
      return FilledButton(
        key: Key('claim-objective-${o.id}'),
        onPressed: anyBusy ? null : onClaim,
        child: AccessibleName(
          name: 'Claim ${o.name}, $reward',
          visible: busy
              ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Claim'),
        ),
      );
    }

    return OutlinedButton(
      key: Key('go-objective-${o.id}'),
      onPressed: onGo,
      child: AccessibleName(
        name: 'Go to $destination for ${o.name}',
        visible: Text('Go to $destination'),
      ),
    );
  }
}
