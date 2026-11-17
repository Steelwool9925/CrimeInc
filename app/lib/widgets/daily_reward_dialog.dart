import 'package:flutter/material.dart';

import '../state/app_state.dart';

/// The daily-login reward prompt. Tapping Claim is what actually takes the reward — there is no
/// decline, since nothing is granted until POST /daily/claim fires. The same button then becomes
/// the acknowledgement once the server has answered, and the reward lines it shows are the
/// server's own numbers, never computed locally.
class DailyRewardDialog extends StatelessWidget {
  const DailyRewardDialog({super.key, required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: appState,
      builder: (context, _) {
        final result = appState.lastDailyClaim;
        final claiming = appState.dailyClaimInFlight;
        final error = appState.dailyClaimError;

        return AlertDialog(
          key: const Key('daily-reward-dialog'),
          title: const Text('Daily Reward'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (result == null)
                const Text('Come back every day for a bigger reward.')
              else ...[
                Text('+\$${result.reward.cash}', key: const Key('daily-reward-cash')),
                if (result.reward.xp > 0) ...[
                  const SizedBox(height: 8),
                  Text('+${result.reward.xp} XP', key: const Key('daily-reward-xp')),
                ],
                const SizedBox(height: 8),
                const Text('Energy refilled', key: Key('daily-reward-energy')),
                if (result.currentStreak == result.longestStreak && result.currentStreak > 1) ...[
                  const SizedBox(height: 8),
                  const Text('Longest streak yet!'),
                ],
              ],
              if (error != null) ...[
                const SizedBox(height: 8),
                Text(error, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
          actions: [
            FilledButton(
              key: const Key('daily-reward-claim'),
              onPressed: claiming
                  ? null
                  : () {
                      if (result == null) {
                        appState.claimDaily();
                      } else {
                        Navigator.of(context).pop();
                        appState.acknowledgeDailyClaim();
                      }
                    },
              child: claiming
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text(result == null ? 'Claim' : 'Nice'),
            ),
          ],
        );
      },
    );
  }
}
