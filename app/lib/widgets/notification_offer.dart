import 'package:flutter/material.dart';

/// A one-line question shown after the player's first finished job, before the OS is asked for
/// notification permission.
///
/// The OS prompt can only be spent once and a refusal is effectively permanent, so the player gets
/// to say "not now" to this without using it up. Sits below the job list rather than above it: the
/// list shrinks at the bottom and nothing the player is aiming at moves.
class NotificationOffer extends StatelessWidget {
  const NotificationOffer({super.key, required this.onYes, required this.onLater});

  final VoidCallback onYes;
  final VoidCallback onLater;

  static const _targetSize = WidgetStatePropertyAll(Size(48, 48));

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: theme.colorScheme.surfaceContainerHigh,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Want a nudge when your energy is full?', style: theme.textTheme.titleSmall),
            const SizedBox(height: 4),
            Text(
              "We'll tell you when you can run jobs again, or when a property has filled up. "
              'You can change this any time on your Rap Sheet.',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 8),
            Align(
              alignment: Alignment.centerRight,
              child: Wrap(
                spacing: 8,
                children: [
                  TextButton(
                    key: const Key('notification-offer-later'),
                    style: const ButtonStyle(minimumSize: _targetSize),
                    onPressed: onLater,
                    child: const Text('Not now'),
                  ),
                  FilledButton(
                    key: const Key('notification-offer-yes'),
                    style: const ButtonStyle(minimumSize: _targetSize),
                    onPressed: onYes,
                    child: const Text('Yes, nudge me'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
