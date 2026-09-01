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
  const AppShell({super.key, required this.appState});

  final AppState appState;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  int _selectedIndex = 0;

  /// Screens are wide enough to earn a side rail at and above this width; narrower gets the
  /// bottom bar instead.
  static const double _wideBreakpoint = 800;

  /// Builds exactly one destination's screen — never all five. Each of these five widgets already
  /// owns a complete `Scaffold`+`AppBar`; that scaffolding is intentionally left in place rather
  /// than stripped out, so a full `Scaffold` here is the correct nested pattern, not a bug.
  Widget _bodyFor(int index) => switch (index) {
        0 => HomeScreen(appState: widget.appState),
        1 => JobsScreen(appState: widget.appState),
        2 => PropertiesScreen(appState: widget.appState),
        3 => const ComingSoonScreen(
            title: 'Crew',
            icon: Icons.groups_outlined,
            message: 'Crew is coming in a future update.',
          ),
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
        items: const [
          BottomNavigationBarItem(
            icon: Icon(Icons.home_outlined),
            label: 'Home',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.work_outline),
            label: 'Jobs',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.location_city_outlined),
            label: 'Props',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.groups_outlined),
            label: 'Crew',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.badge_outlined),
            label: 'Profile',
          ),
        ],
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
            destinations: const [
              NavigationRailDestination(
                icon: Icon(Icons.home_outlined),
                label: Text('Home'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.work_outline),
                label: Text('Jobs'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.location_city_outlined),
                label: Text('Properties'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.groups_outlined),
                label: Text('Crew'),
              ),
              NavigationRailDestination(
                icon: Icon(Icons.badge_outlined),
                label: Text('Profile'),
              ),
            ],
          ),
          const VerticalDivider(width: 1),
          Expanded(child: _bodyFor(_selectedIndex)),
        ],
      ),
    );
  }
}
