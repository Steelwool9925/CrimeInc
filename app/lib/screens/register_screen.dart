import 'package:flutter/material.dart';

import '../state/app_state.dart';

/// Username/password entry, pushed from [ProfileScreen]'s Account section. Calls
/// [AppState.registerAccount]; on success pops back to Profile, which reads the updated
/// `me.username` straight from [AppState] — no separate confirmation state to thread back.
class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key, required this.appState});

  final AppState appState;

  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final ok = await widget.appState.registerAccount(
      _usernameController.text.trim(),
      _passwordController.text,
    );
    if (ok && mounted) Navigator.of(context).pop();
    // On failure, appState.registerAccountError is set and ListenableBuilder below re-renders
    // it — nothing further to do here.
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Create Account')),
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
                const Text(
                  'Protect this save so you can log into it from another device.',
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 16),
                TextField(
                  key: const Key('register-username-field'),
                  controller: _usernameController,
                  decoration: const InputDecoration(
                    labelText: 'Username',
                    helperText: '3-32 letters, digits, underscores or hyphens',
                    border: OutlineInputBorder(),
                  ),
                  enabled: !state.registeringAccount,
                ),
                const SizedBox(height: 8),
                TextField(
                  key: const Key('register-password-field'),
                  controller: _passwordController,
                  decoration: const InputDecoration(
                    labelText: 'Password',
                    helperText: 'At least 8 characters',
                    border: OutlineInputBorder(),
                  ),
                  obscureText: true,
                  enabled: !state.registeringAccount,
                  onSubmitted: (_) => state.registeringAccount ? null : _submit(),
                ),
                if (state.registerAccountError != null) ...[
                  const SizedBox(height: 8),
                  Text(
                    state.registerAccountError!,
                    key: const Key('register-error'),
                    style: TextStyle(color: Theme.of(context).colorScheme.error),
                  ),
                ],
                const SizedBox(height: 16),
                ElevatedButton(
                  key: const Key('register-submit'),
                  onPressed: state.registeringAccount ? null : _submit,
                  child: state.registeringAccount
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Create Account'),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
