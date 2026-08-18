import 'package:flutter/material.dart';

import '../api/models.dart';

/// The level-up celebration. Reports what the level actually granted, since the meter refill and the
/// skill point are the mechanical rewards a player needs to know about.
class LevelUpDialog extends StatelessWidget {
  const LevelUpDialog({
    super.key,
    required this.levelUp,
    required this.onAcknowledge,
  });

  final LevelUp levelUp;
  final VoidCallback onAcknowledge;

  @override
  Widget build(BuildContext context) {
    final points = levelUp.skillPointsGranted;

    return AlertDialog(
      key: const Key('level-up-dialog'),
      title: Text(
        levelUp.levelsGained > 1
            ? 'Level ${levelUp.newLevel}! (+${levelUp.levelsGained} levels)'
            : 'Level ${levelUp.newLevel}!',
      ),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(points == 1
              ? 'You earned 1 skill point.'
              : 'You earned $points skill points.'),
          if (levelUp.metersRefilled) ...[
            const SizedBox(height: 8),
            const Text('Energy, stamina and health refilled.'),
          ],
        ],
      ),
      actions: [
        TextButton(
          key: const Key('level-up-ok'),
          onPressed: onAcknowledge,
          child: const Text('Nice'),
        ),
      ],
    );
  }
}
