import 'package:flutter/material.dart';

/// A reusable, honest placeholder screen for features that don't exist yet.
///
/// This is pure UI — no `AppState`, no network dependency, no loading state. It exists so a
/// screen route can be wired up ahead of the feature it represents (e.g. Crew, Territory)
/// without pretending the feature is live.
class ComingSoonScreen extends StatelessWidget {
  const ComingSoonScreen({
    super.key,
    required this.title,
    required this.icon,
    required this.message,
  });

  final String title;
  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(title)),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                icon,
                size: 64,
                key: const Key('coming-soon-icon'),
              ),
              const SizedBox(height: 16),
              Text(
                title,
                style: Theme.of(context).textTheme.headlineSmall,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                message,
                key: const Key('coming-soon-message'),
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The Crew placeholder, shared so its title/icon/message are declared exactly once. Both the
/// Home dashboard tile and the app shell's Crew tab route here, rather than each constructing
/// their own copy of the same three literals.
const ComingSoonScreen crewComingSoonScreen = ComingSoonScreen(
  title: 'Crew',
  icon: Icons.groups_outlined,
  message: 'Crew is coming in a future update.',
);
