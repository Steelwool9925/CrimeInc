import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'api/api_client.dart';
import 'screens/server_address_screen.dart';
import 'state/app_state.dart';
import 'storage/server_address_store.dart';
import 'storage/token_store.dart';
import 'theme/pack_theme.dart';
import 'widgets/app_shell.dart';

void main() {
  runApp(const UnderworldEmpireApp());
}

class UnderworldEmpireApp extends StatefulWidget {
  const UnderworldEmpireApp({super.key});

  @override
  State<UnderworldEmpireApp> createState() => _UnderworldEmpireAppState();
}

class _UnderworldEmpireAppState extends State<UnderworldEmpireApp> {
  final ServerAddressStore _serverAddressStore = SharedPrefsServerAddressStore();
  final TokenStore _tokenStore = SharedPrefsTokenStore();

  // Null until a server address is known — either read from storage at startup, or entered on
  // ServerAddressScreen just now. AppState/HttpApiClient cannot be constructed before then: the
  // base URL is a constructor argument, not something set later.
  AppState? _appState;

  // True only while resolving the stored address at startup, so the very first frame doesn't
  // flash ServerAddressScreen before storage has actually been read.
  bool _resolvingStartup = true;

  @override
  void initState() {
    super.initState();
    _resolveStartup();
  }

  Future<void> _resolveStartup() async {
    final address = await _serverAddressStore.read();
    if (address != null && address.isNotEmpty) {
      _startAppState(address);
    } else {
      setState(() => _resolvingStartup = false);
    }
  }

  /// Builds and bootstraps [AppState] against [address], matching exactly what this file did
  /// unconditionally before this gate existed — the only thing new is that [address] is no longer
  /// hardcoded/compile-time, it comes from storage or from [ServerAddressScreen].
  void _startAppState(String address) {
    final appState = AppState(
      apiClient: HttpApiClient(client: http.Client(), baseUrl: address),
      tokenStore: _tokenStore,
    );
    appState.bootstrap();
    appState.startPolling();
    setState(() {
      _appState = appState;
      _resolvingStartup = false;
    });
  }

  Future<void> _onAddressSaved(String address) async {
    await _serverAddressStore.write(address);
    _startAppState(address);
  }

  @override
  void dispose() {
    _appState?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Before a server address is known, there is no ContentPack to theme from yet — packTheme(null)
    // is exactly the fallback palette it already returns for that case, so both branches below look
    // the same as the app's ordinary pre-load frame.
    if (_resolvingStartup) {
      return MaterialApp(
        theme: packTheme(null),
        home: const Scaffold(body: Center(child: CircularProgressIndicator())),
      );
    }

    final appState = _appState;
    if (appState == null) {
      return MaterialApp(
        theme: packTheme(null),
        home: ServerAddressScreen(onSaved: _onAddressSaved),
      );
    }

    // Rebuilt when the pack arrives: the theme is content, so the whole app re-colours the
    // moment GET /content/pack lands rather than only the one app bar that used to read it.
    return ListenableBuilder(
      listenable: appState,
      builder: (context, _) {
        final pack = appState.contentPack;
        return MaterialApp(
          title: pack?.name.isNotEmpty == true ? pack!.name : 'Underworld Empire',
          theme: packTheme(pack),
          home: AppShell(appState: appState),
        );
      },
    );
  }
}
