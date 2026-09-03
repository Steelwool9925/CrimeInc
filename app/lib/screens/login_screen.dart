import 'package:flutter/material.dart';

import '../state/app_state.dart';

/// Username/password entry, pushed from [WelcomeScreen]. Calls [AppState.login]; on success calls
/// [onLoggedIn] so `main.dart` can move past the startup gate the same way it does for a guest.
class LoginScreen extends StatefulWidget {
  const LoginScreen({
    super.key,
    required this.appState,
    required this.onLoggedIn,
  });

  final AppState appState;
  final VoidCallback onLoggedIn;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final ok = await widget.appState.login(
      _usernameController.text.trim(),
      _passwordController.text,
    );
    if (ok) widget.onLoggedIn();
    // On failure, appState.loginError is set and ListenableBuilder below re-renders it — nothing
    // further to do here.
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Log In')),
      body: ListenableBuilder(
        listenable: widget.appState,
        builder: (context, _) {
          final state = widget.appState;

          return Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextField(
                  key: const Key('login-username-field'),
                  controller: _usernameController,
                  decoration: const InputDecoration(
                    labelText: 'Username',
                    border: OutlineInputBorder(),
                  ),
                  enabled: !state.loggingIn,
                ),
                const SizedBox(height: 8),
                TextField(
                  key: const Key('login-password-field'),
                  controller: _passwordController,
                  decoration: const InputDecoration(
                    labelText: 'Password',
                    border: OutlineInputBorder(),
                  ),
                  obscureText: true,
                  enabled: !state.loggingIn,
                  onSubmitted: (_) => state.loggingIn ? null : _submit(),
                ),
                if (state.loginError != null) ...[
                  const SizedBox(height: 8),
                  Text(
                    state.loginError!,
                    key: const Key('login-error'),
                    style: TextStyle(color: Theme.of(context).colorScheme.error),
                  ),
                ],
                const SizedBox(height: 16),
                ElevatedButton(
                  key: const Key('login-submit'),
                  onPressed: state.loggingIn ? null : _submit,
                  child: state.loggingIn
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Log In'),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
