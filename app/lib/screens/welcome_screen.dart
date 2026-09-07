import 'package:flutter/material.dart';

import '../state/app_state.dart';
import 'login_screen.dart';

/// Shown once — after a server address is known but before any player identity exists on this
/// device — offering the two ways to get one: start fresh as a guest, or log into an account that
/// already has a save elsewhere.
///
/// A startup gate in `main.dart`, not a pushed route, same as [ServerAddressScreen] before it:
/// there is nothing to navigate back to yet.
class WelcomeScreen extends StatelessWidget {
  const WelcomeScreen({
    super.key,
    required this.appState,
    required this.onContinueAsGuest,
    required this.onLoggedIn,
  });

  final AppState appState;

  /// Called when the player picks "Continue as new player" — `main.dart` starts the guest
  /// bootstrap and moves past this gate.
  final VoidCallback onContinueAsGuest;

  /// Called once [LoginScreen] reports a successful login — `main.dart` moves past this gate the
  /// same way [onContinueAsGuest] does.
  final VoidCallback onLoggedIn;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Welcome')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text(
              'Is this your first time here, or do you already have a save?',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 24),
            ElevatedButton(
              key: const Key('welcome-continue-as-guest'),
              onPressed: onContinueAsGuest,
              child: const Text('Continue as new player'),
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              key: const Key('welcome-log-in'),
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) => LoginScreen(
                    appState: appState,
                    onLoggedIn: onLoggedIn,
                  ),
                ),
              ),
              child: const Text('Log in to an existing account'),
            ),
          ],
        ),
      ),
    );
  }
}
