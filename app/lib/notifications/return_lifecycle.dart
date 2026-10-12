import 'package:flutter/widgets.dart';

import '../state/app_state.dart';

/// Schedules the return nudges as the app goes to the background and clears them when the player
/// is back.
///
/// Wraps the signed-in app, so it only ever acts on a player who has a snapshot to schedule from.
/// Coming back also re-reads the property listing — the one snapshot nothing else refreshes on its
/// own, and what the next backgrounding schedules from — but only for a player who will be told
/// about it. Clearing the nudges is unconditional; there is nothing to gain by leaving a stale one
/// in the tray.
class ReturnNotifierLifecycle extends StatefulWidget {
  const ReturnNotifierLifecycle({super.key, required this.appState, required this.child});

  final AppState appState;
  final Widget child;

  @override
  State<ReturnNotifierLifecycle> createState() => _ReturnNotifierLifecycleState();
}

class _ReturnNotifierLifecycleState extends State<ReturnNotifierLifecycle>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    switch (state) {
      case AppLifecycleState.paused:
        widget.appState.scheduleReturnNudges();
      case AppLifecycleState.resumed:
        widget.appState.cancelReturnNudges();
        widget.appState.refreshNudgeSnapshot();
      case AppLifecycleState.inactive:
      case AppLifecycleState.hidden:
      case AppLifecycleState.detached:
        break;
    }
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
