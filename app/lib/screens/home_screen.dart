import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';
import '../theme/pack_theme.dart';
import '../widgets/coming_soon_screen.dart';
import '../widgets/halftone_wash.dart';
import '../widgets/meter_card.dart';
import '../widgets/pack_logo.dart';
import '../widgets/speedline_divider.dart';
import 'jobs_screen.dart';
import 'profile_screen.dart';
import 'properties_screen.dart';
import 'skills_screen.dart';
import 'store_screen.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, required this.appState, required this.onChangeServer});

  final AppState appState;

  /// Escapes a stuck server address — e.g. a friend's tunnel URL that has since rotated (quick
  /// tunnels mint a fresh hostname every restart; see docs/hosting-a-friend.md) — back to
  /// ServerAddressScreen. Without this, a permanently unreachable host has no recovery: Retry can
  /// only ever hit the same dead hostname again.
  final VoidCallback onChangeServer;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: appState,
      builder: (context, _) {
        final pack = appState.contentPack;

        return Scaffold(
          appBar: AppBar(
            // The pack supplies its own branding: its logo where it ships one, its name where
            // it does not. Neither is hardcoded, so a different pack rebrands the app without
            // a code change. Colours are left unset so the AppBar inherits the dark
            // Steamnoir AppBarTheme from pack_theme.dart.
            title: PackLogo(appState: appState, pack: pack),
            // Atmosphere only -- flexibleSpace paints behind the title/actions, so the wash
            // never affects hit-testing or the logo's legibility.
            flexibleSpace: HalftoneWash(color: Theme.of(context).colorScheme.onSurface),
          ),
          body: _buildBody(context, appState),
        );
      },
    );
  }

  Widget _buildBody(BuildContext context, AppState state) {
    switch (state.status) {
      case LoadStatus.loading:
        return const Center(child: CircularProgressIndicator());
      case LoadStatus.error:
        return Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                state.errorMessage ?? 'An error occurred',
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              const SizedBox(height: 16),
              ElevatedButton(
                onPressed: appState.bootstrap,
                child: const Text('Retry'),
              ),
              const SizedBox(height: 8),
              OutlinedButton(
                key: const Key('change-server'),
                onPressed: onChangeServer,
                child: const Text('Change Server'),
              ),
            ],
          ),
        );
      case LoadStatus.ready:
        return _buildReadyBody(context, state);
    }
  }

  Widget _buildReadyBody(BuildContext context, AppState state) {
    final me = state.me!;
    final pack = state.contentPack;
    final cashLabel = pack?.cashLabel ?? 'Cash';

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          MeterCard(label: 'Energy', meter: me.energy),
          const SizedBox(height: 8),
          MeterCard(label: 'Stamina', meter: me.stamina),
          const SizedBox(height: 8),
          MeterCard(label: 'Health', meter: me.health),
          const SizedBox(height: 16),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    cashLabel,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    '${me.cash}',
                    style: statNumberStyle(context, fontSize: 20),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 8),
          _ProgressionCard(me: me),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            key: const Key('go-to-skills'),
            icon: const Icon(Icons.fitness_center),
            label: Text(
              me.skillPoints > 0 ? 'Skills (${me.skillPoints})' : 'Skills',
            ),
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute(builder: (_) => SkillsScreen(appState: appState)),
            ),
          ),
          const SizedBox(height: 16),
          // Marks the section break between "your character" (meters, cash, progression) and
          // "where to go next" (the dashboard) -- used once, not on every divider, so it still
          // reads as a scene break rather than becoming background noise.
          SpeedlineDivider(color: Theme.of(context).colorScheme.onSurface),
          const SizedBox(height: 16),
          _DashboardGrid(appState: appState),
        ],
      ),
    );
  }
}

/// The 6-tile dashboard: every place Home can send the player, one tile each. Two of the six
/// (Crew, Territory) route to [ComingSoonScreen] because those features don't exist yet — the
/// tile still gets a subtitle saying so, rather than pretending they're live or hiding them.
///
/// **Live destinations come first; the "Coming soon" tiles sort last.** Order is load-bearing on a
/// tall viewport, where only the first row or two is above the fold: Market used to sit *below*
/// the dead Crew tile, so a player had to scroll past a feature that does not exist to reach one
/// that does. That matters more now that a first session earns enough to shop. Keep every live
/// tile ahead of every placeholder when adding to this list.
class _DashboardGrid extends StatelessWidget {
  const _DashboardGrid({required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 2,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      childAspectRatio: 1.3,
      children: [
        _DashboardTile(
          tileKey: const Key('go-to-jobs'),
          icon: Icons.work_outline,
          title: 'Jobs',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => JobsScreen(appState: appState)),
          ),
        ),
        _DashboardTile(
          tileKey: const Key('go-to-properties'),
          icon: Icons.location_city_outlined,
          title: 'Properties',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(
                builder: (_) => PropertiesScreen(appState: appState)),
          ),
        ),
        _DashboardTile(
          tileKey: const Key('go-to-store'),
          icon: Icons.storefront_outlined,
          title: 'Market',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => StoreScreen(appState: appState)),
          ),
        ),
        _DashboardTile(
          tileKey: const Key('go-to-profile'),
          icon: Icons.badge_outlined,
          title: 'Rap Sheet',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => ProfileScreen(appState: appState)),
          ),
        ),
        _DashboardTile(
          tileKey: const Key('go-to-crew'),
          icon: Icons.groups_outlined,
          title: 'Crew',
          subtitle: 'Coming soon',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(builder: (_) => crewComingSoonScreen),
          ),
        ),
        _DashboardTile(
          tileKey: const Key('go-to-territory'),
          icon: Icons.map_outlined,
          title: 'Territory',
          subtitle: 'Coming soon',
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute(
              builder: (_) => const ComingSoonScreen(
                title: 'Territory',
                icon: Icons.map_outlined,
                message: 'Territory is coming in a future update.',
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _DashboardTile extends StatelessWidget {
  const _DashboardTile({
    required this.tileKey,
    required this.icon,
    required this.title,
    required this.onTap,
    this.subtitle,
  });

  final Key tileKey;
  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  /// Whether this tile routes to a feature that doesn't exist yet.
  bool get _isComingSoon => subtitle != null;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // A "coming soon" tile is present but not actionable, not an error -- it gets a dimmer
    // outline instead of the theme's one spot colour, which stays reserved for danger/CTA/active
    // states. The ink-outline width itself (from CardTheme) is left alone; only its colour dims.
    final dimBorder = theme.colorScheme.onSurface.withValues(alpha: 0.35);
    final dimContent = theme.colorScheme.onSurface.withValues(alpha: 0.5);

    return Card(
      key: tileKey,
      shape: _isComingSoon
          ? (theme.cardTheme.shape as RoundedRectangleBorder?)
              ?.copyWith(side: BorderSide(color: dimBorder, width: 2.5))
          : null,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 32, color: _isComingSoon ? dimContent : null),
              const SizedBox(height: 8),
              Text(
                title,
                textAlign: TextAlign.center,
                style: theme.textTheme.titleMedium?.copyWith(
                  color: _isComingSoon ? dimContent : null,
                ),
              ),
              if (subtitle != null) ...[
                const SizedBox(height: 4),
                Text(
                  subtitle!,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodySmall?.copyWith(color: dimContent),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Level, XP progress toward the next level, and the combat stats a skill point moves.
class _ProgressionCard extends StatelessWidget {
  const _ProgressionCard({required this.me});

  final Me me;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('Level', style: theme.textTheme.titleMedium),
                Text(
                  '${me.level}',
                  key: const Key('home-level'),
                  style: statNumberStyle(context, fontSize: 20),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text('${me.xp} XP', key: const Key('home-xp'), style: theme.textTheme.bodySmall),
            const SizedBox(height: 8),
            Row(
              children: [
                // "Base", explicitly: these come from /me and exclude equipped gear, while
                // Inventory shows the effective total. Unlabelled, the two screens read as a
                // contradiction and an equip looks like it failed (A.5.1).
                Expanded(
                  child: Text(
                    'Base attack ${me.attack}',
                    key: const Key('home-attack'),
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                Expanded(
                  child: Text(
                    'Base defense ${me.defense}',
                    key: const Key('home-defense'),
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
              ],
            ),
            if (me.skillPoints > 0) ...[
              const SizedBox(height: 8),
              Text(
                '${me.skillPoints} skill point${me.skillPoints == 1 ? '' : 's'} to spend',
                key: const Key('home-skill-points'),
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.bold),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
