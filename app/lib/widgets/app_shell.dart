import 'package:flutter/material.dart';

import '../screens/home_screen.dart';
import '../screens/jobs_screen.dart';
import '../screens/profile_screen.dart';
import '../screens/properties_screen.dart';
import '../state/app_state.dart';
import 'coming_soon_screen.dart';

/// The app's persistent navigation chrome: a bottom bar on narrow screens, a side rail on wide
/// ones, wrapping the five top-level destinations.
///
/// This is the first responsive/breakpoint code in the app, and deliberately stays that small —
/// no router, no DI container. `AppState` is still hand-wired at the root exactly as it was
/// before this shell existed; this widget only decides which of the five already-complete screens
/// is on screen.
class AppShell extends StatefulWidget {
  const AppShell({super.key, required this.appState, required this.onChangeServer});

  final AppState appState;

  /// Forwarded to Home's error screen — see [HomeScreen.onChangeServer].
  final VoidCallback onChangeServer;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;

  /// Screens are wide enough to earn a side rail at and above this width; narrower gets the
  /// bottom bar instead.
  static const double _wideBreakpoint = 800;

  /// The five top-level destinations, declared once. `_buildNarrow` and `_buildWide` each map
  /// this into their own destination widget type, so adding, renaming, or reordering a
  /// destination only has to happen here. Narrow and wide labels are kept distinct because the
  /// two layouts intentionally disagree on some of them (e.g. 'Props' vs 'Properties').
  static const List<({IconData icon, String narrowLabel, String wideLabel})>
      _destinations = [
    (icon: Icons.home_outlined, narrowLabel: 'Home', wideLabel: 'Home'),
    (icon: Icons.work_outline, narrowLabel: 'Jobs', wideLabel: 'Jobs'),
    (
      icon: Icons.location_city_outlined,
      narrowLabel: 'Props',
      wideLabel: 'Properties',
    ),
    (icon: Icons.groups_outlined, narrowLabel: 'Crew', wideLabel: 'Crew'),
    (icon: Icons.badge_outlined, narrowLabel: 'Profile', wideLabel: 'Profile'),
  ];

  /// Builds exactly one destination's screen — never all five. Each of these five widgets already
  /// owns a complete `Scaffold`+`AppBar`; that scaffolding is intentionally left in place rather
  /// than stripped out, so a full `Scaffold` here is the correct nested pattern, not a bug.
  Widget _bodyFor(int index) => switch (index) {
        0 => HomeScreen(appState: widget.appState, onChangeServer: widget.onChangeServer),
        1 => JobsScreen(appState: widget.appState),
        2 => PropertiesScreen(appState: widget.appState),
        3 => crewComingSoonScreen,
        4 => ProfileScreen(appState: widget.appState),
        _ => throw StateError('unknown destination index: $index'),
      };

  void _onSelect(int index) => setState(() => _selectedIndex = index);

  @override
  Widget build(BuildContext context) {
    final wide = MediaQuery.sizeOf(context).width >= _wideBreakpoint;

    return wide ? _buildWide(context) : _buildNarrow(context);
  }

  Widget _buildNarrow(BuildContext context) {
    return Scaffold(
      key: const Key('app-shell-narrow'),
      body: _bodyFor(_selectedIndex),
      bottomNavigationBar: BottomNavigationBar(
        // The theme already styles BottomNavigationBar via bottomNavigationBarTheme.
        currentIndex: _selectedIndex,
        onTap: _onSelect,
        items: _destinations
            .map((d) => BottomNavigationBarItem(
                  icon: Icon(d.icon),
                  label: d.narrowLabel,
                ))
            .toList(),
      ),
    );
  }

  Widget _buildWide(BuildContext context) {
    return Scaffold(
      key: const Key('app-shell-wide'),
      body: Row(
        children: [
          NavigationRail(
            // The theme already styles NavigationRail via navigationRailTheme.
            selectedIndex: _selectedIndex,
            onDestinationSelected: _onSelect,
            destinations: _destinations
                .map((d) => NavigationRailDestination(
                      icon: Icon(d.icon),
                      label: Text(d.wideLabel),
                    ))
                .toList(),
          ),
          const VerticalDivider(width: 1),
          Expanded(child: _bodyFor(_selectedIndex)),
        ],
      ),
    );
  }
}
