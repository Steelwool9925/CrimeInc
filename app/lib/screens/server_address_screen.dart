import 'package:flutter/material.dart';

/// Whether [host] is `localhost`, a loopback address, or falls inside a private (RFC 1918) IPv4
/// range — the addresses a plain `http://` connection is acceptable for, because the traffic
/// never leaves a trusted local network.
bool isPrivateOrLocalHost(String host) {
  if (host == 'localhost' || host == '127.0.0.1') return true;

  final parts = host.split('.');
  if (parts.length != 4) return false;

  final octets = <int>[];
  for (final part in parts) {
    final value = int.tryParse(part);
    if (value == null || value < 0 || value > 255) return false;
    octets.add(value);
  }

  if (octets[0] == 10) return true; // 10.0.0.0/8
  if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) return true; // 172.16.0.0/12
  if (octets[0] == 192 && octets[1] == 168) return true; // 192.168.0.0/16

  return false;
}

/// Whether [input] is an acceptable server address: a valid `http`/`https` URL, and — this is the
/// mandatory security check — `https://` unless the host is local or private per
/// [isPrivateOrLocalHost].
///
/// A friend's tunnel URL is reachable by anyone who has it and, once accounts exist, carries a
/// real password on login. A plain `http://` address pointed at the open internet would send that
/// password in cleartext; refusing to save one here — before the player ever gets the chance to
/// type a password into it — is cheaper than any server-side mitigation.
bool isAcceptableServerAddress(String input) {
  final trimmed = input.trim();
  if (trimmed.isEmpty) return false;

  final uri = Uri.tryParse(trimmed);
  if (uri == null || uri.host.isEmpty) return false;
  if (uri.scheme != 'http' && uri.scheme != 'https') return false;

  if (uri.scheme == 'https') return true;

  return isPrivateOrLocalHost(uri.host);
}

/// Shown once, at first launch, before any player identity exists: where is the server?
///
/// This is a startup gate in `main.dart`, not a pushed route — there is nothing to navigate back
/// to yet. [onSaved] is called with the validated, trimmed address; the caller is responsible for
/// persisting it and moving past this screen.
class ServerAddressScreen extends StatefulWidget {
  const ServerAddressScreen({super.key, required this.onSaved});

  final Future<void> Function(String address) onSaved;

  @override
  State<ServerAddressScreen> createState() => _ServerAddressScreenState();
}

class _ServerAddressScreenState extends State<ServerAddressScreen> {
  final _controller = TextEditingController();
  String? _error;
  bool _saving = false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final address = _controller.text.trim();

    if (!isAcceptableServerAddress(address)) {
      setState(() {
        _error = address.isEmpty
            ? 'Enter a server address.'
            : 'Must be a valid http:// or https:// address. A non-local address must use '
                'https:// — a friend\'s tunnel URL is reachable by anyone who has it, and '
                'account passwords must never travel in plain http.';
      });
      return;
    }

    setState(() {
      _error = null;
      _saving = true;
    });

    // Close the text-input connection before handing off. On web, Flutter keeps a real DOM input
    // alive for the focused field; leaving this screen with the field still focused left that
    // element — and the form wrapper around it — in the accessibility tree for the rest of the
    // session. A screen-reader or keyboard user deep in the game could still tab onto a server
    // address box belonging to a screen that is long gone.
    FocusManager.instance.primaryFocus?.unfocus();

    await widget.onSaved(address);
    // No further setState here: onSaved moves the app past this screen entirely, so this widget
    // is expected to be gone by the time the future completes.
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Server Address')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Text(
              'Enter the address of the server you want to play on.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            TextField(
              key: const Key('server-address-field'),
              controller: _controller,
              decoration: const InputDecoration(
                labelText: 'Server address',
                hintText: 'https://your-server.example.com',
                border: OutlineInputBorder(),
              ),
              keyboardType: TextInputType.url,
              onSubmitted: (_) => _saving ? null : _save(),
            ),
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(
                _error!,
                key: const Key('server-address-error'),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ],
            const SizedBox(height: 16),
            ElevatedButton(
              key: const Key('server-address-save'),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Continue'),
            ),
          ],
        ),
      ),
    );
  }
}
