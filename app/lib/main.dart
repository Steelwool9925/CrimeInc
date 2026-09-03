import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'api/api_client.dart';
import 'screens/server_address_screen.dart';
import 'screens/welcome_screen.dart';
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

  // True only while resolving stored startup state, so the very first frame doesn't flash
  // ServerAddressScreen/WelcomeScreen before storage has actually been read.
  bool _resolvingStartup = true;

  // True once a server address is known but no device token was found in storage — the app needs
  // the player to choose "continue as guest" or "log in" on WelcomeScreen before AppState ever
  // calls bootstrap()/login().
  bool _needsWelcomeChoice = false;

  @override
  void initState() {
    super.initState();
    _resolveStartup();
  }

  Future<void> _resolveStartup() async {
    final address = await _serverAddressStore.read();
    if (address == null || address.isEmpty) {
      setState(() => _resolvingStartup = false);
      return;
    }

    final storedToken = await _tokenStore.read();
    if (storedToken != null && storedToken.isNotEmpty) {
      // Returning player: same fast path this file always took, before this gate existed.
      _startAppState(address);
    } else {
      setState(() {
        _appState = _buildAppState(address);
        _needsWelcomeChoice = true;
        _resolvingStartup = false;
      });
    }
  }

  AppState _buildAppState(String address) => AppState(
        apiClient: HttpApiClient(client: http.Client(), baseUrl: address),
        tokenStore: _tokenStore,
      );

  /// Builds [AppState] against [address] and bootstraps it immediately — matching exactly what
  /// this file did unconditionally before this gate existed. Used by the returning-player fast
  /// path in [_resolveStartup] and by "continue as guest" on [WelcomeScreen].
  void _startAppState(String address) {
    final appState = _buildAppState(address);
    appState.bootstrap();
    appState.startPolling();
    setState(() {
      _appState = appState;
      _needsWelcomeChoice = false;
      _resolvingStartup = false;
    });
  }

  Future<void> _onAddressSaved(String address) async {
    await _serverAddressStore.write(address);
    // A freshly-entered address never already has a token waiting for it — this is always a
    // first-time player, so the Welcome/Login gate is next, never the returning-player fast path.
    setState(() {
      _appState = _buildAppState(address);
      _needsWelcomeChoice = true;
    });
  }

  void _continueAsGuest() {
    final appState = _appState!;
    appState.bootstrap();
    appState.startPolling();
    setState(() => _needsWelcomeChoice = false);
  }

  void _onLoggedIn() {
    // AppState.login already ran _finishAuth (token persisted, pack + player loaded, status
    // ready) by the time WelcomeScreen/LoginScreen call this — only polling and the gate flag
    // are this file's responsibility.
    _appState!.startPolling();
    setState(() => _needsWelcomeChoice = false);
  }

  @override
  void dispose() {
    _appState?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Before a server address is known, there is no ContentPack to theme from yet — packTheme(null)
    // is exactly the fallback palette it already returns for that case, so every branch below
    // looks the same as the app's ordinary pre-load frame.
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

    if (_needsWelcomeChoice) {
      return MaterialApp(
        theme: packTheme(null),
        home: WelcomeScreen(
          appState: appState,
          onContinueAsGuest: _continueAsGuest,
          onLoggedIn: _onLoggedIn,
        ),
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
