import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'api/api_client.dart';
import 'state/app_state.dart';
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
  late final AppState _appState;

  @override
  void initState() {
    super.initState();
    _appState = AppState(
      apiClient: HttpApiClient(client: http.Client()),
      tokenStore: SharedPrefsTokenStore(),
    );
    _appState.bootstrap();
    _appState.startPolling();
  }

  @override
  void dispose() {
    _appState.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // Rebuilt when the pack arrives: the theme is content, so the whole app re-colours the
    // moment GET /content/pack lands rather than only the one app bar that used to read it.
    return ListenableBuilder(
      listenable: _appState,
      builder: (context, _) {
        final pack = _appState.contentPack;
        return MaterialApp(
          title: pack?.name.isNotEmpty == true ? pack!.name : 'Underworld Empire',
          theme: packTheme(pack),
          home: AppShell(appState: _appState),
        );
      },
    );
  }
}
