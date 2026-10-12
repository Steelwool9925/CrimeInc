import 'package:flutter/material.dart';

import '../state/app_state.dart';
import 'register_screen.dart';
import 'skills_screen.dart';

/// The "Rap Sheet" screen — deliberately scoped to what the server already models.
///
/// The design reference for this screen shows a player display name, a rank title, a respect
/// meter, an avatar/plate picker and an achievements grid. Only the first of those exists
/// server-side now — an account's username, once claimed (see [_AccountSection]) — and even that
/// is a login credential, not the freeform display name the design shows. Rank, respect, an
/// avatar/plate picker and achievements still don't exist at all, so none of them are invented
/// here; this screen shows a plain panel saying they are not live yet, rather than fabricating
/// placeholder values that would read as real.
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key, required this.appState});

  final AppState appState;

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  @override
  void initState() {
    super.initState();
    // This screen can be opened without Jobs ever having been visited, and
    // AppState.totalJobsCompleted needs the job catalog to have been fetched at least once. Load
    // after the first frame so the screen can show its loading state, same as StoreScreen does for
    // its own catalog. It's fine for Jobs Done to read 0 until this resolves.
    WidgetsBinding.instance.addPostFrameCallback((_) => widget.appState.loadJobs());
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.appState,
      builder: (context, _) {
        final state = widget.appState;

        return Scaffold(
          appBar: AppBar(title: const Text('Rap Sheet')),
          body: SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Center(child: _Portrait(appState: state)),
                const SizedBox(height: 24),
                // Profile is always pushed after Home has bootstrapped, but a defensive null check
                // costs nothing and matches how the rest of the app treats a not-yet-loaded Me.
                if (state.me == null)
                  const Center(child: CircularProgressIndicator())
                else
                  _StatTiles(appState: state),
                const SizedBox(height: 16),
                OutlinedButton.icon(
                  key: const Key('profile-go-to-skills'),
                  icon: const Icon(Icons.fitness_center),
                  label: const Text('Skills'),
                  onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => SkillsScreen(appState: state)),
                  ),
                ),
                const SizedBox(height: 16),
                _AccountSection(appState: state),
                const SizedBox(height: 16),
                _SoundToggle(appState: state),
                const SizedBox(height: 16),
                const _ComingSoonPanel(),
              ],
            ),
          ),
        );
      },
    );
  }
}

/// A bordered square portrait box. Shows the pack's own icon art when the manifest declares one,
/// otherwise (or on load failure) a generic person glyph — never a name or rank near it.
class _Portrait extends StatelessWidget {
  const _Portrait({required this.appState});

  final AppState appState;

  static const double _size = 120;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final pack = appState.contentPack;

    return Container(
      width: _size,
      height: _size,
      decoration: BoxDecoration(
        color: theme.cardColor,
        border: Border.all(color: theme.dividerColor),
      ),
      child: (pack != null && pack.hasAsset('icon'))
          ? Image.network(
              appState.assetUrl('icon'),
              key: const Key('profile-portrait-image'),
              fit: BoxFit.contain,
              // A content pack is data supplied from outside the app: missing or broken art must
              // never surface a broken-image glyph — the same rule PackLogo follows for the app
              // bar's logo.
              errorBuilder: (context, _, __) => const _PortraitFallback(),
            )
          : const _PortraitFallback(),
    );
  }
}

class _PortraitFallback extends StatelessWidget {
  const _PortraitFallback();

  @override
  Widget build(BuildContext context) {
    return const Center(child: Icon(Icons.person_outline, size: 64));
  }
}

/// Four small stat cards, each carrying a value this app actually tracks.
class _StatTiles extends StatelessWidget {
  const _StatTiles({required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    final me = appState.me;
    final cashLabel = appState.contentPack?.cashLabel ?? 'Cash';

    return Wrap(
      alignment: WrapAlignment.center,
      spacing: 8,
      runSpacing: 8,
      children: [
        _StatTile(
          key: const Key('profile-stat-cash'),
          label: cashLabel,
          value: '${me?.cash ?? 0}',
        ),
        _StatTile(
          key: const Key('profile-stat-level'),
          label: 'Level',
          value: '${me?.level ?? 0}',
        ),
        _StatTile(
          key: const Key('profile-stat-jobs-done'),
          label: 'Jobs Done',
          value: '${appState.totalJobsCompleted}',
        ),
        _StatTile(
          key: const Key('profile-stat-skill-points'),
          label: 'Skill Points',
          value: '${me?.skillPoints ?? 0}',
        ),
      ],
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({super.key, required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return SizedBox(
      width: 150,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(label, style: theme.textTheme.bodySmall),
              const SizedBox(height: 4),
              Text(value, style: theme.textTheme.titleLarge),
            ],
          ),
        ),
      ),
    );
  }
}

/// Real account state, not a placeholder: an unclaimed guest player gets a "protect this save"
/// prompt into [RegisterScreen]; a claimed one shows the username it's logged in as.
class _AccountSection extends StatelessWidget {
  const _AccountSection({required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    final username = appState.me?.username;

    if (username != null) {
      return Card(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Text(
            'Logged in as $username',
            key: const Key('profile-account-username'),
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ),
      );
    }

    return OutlinedButton.icon(
      key: const Key('profile-create-account'),
      icon: const Icon(Icons.lock_outline),
      label: const Text('Create Account'),
      onPressed: () => Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => RegisterScreen(appState: appState)),
      ),
    );
  }
}

/// The mute for job cues. Lives here because Rap Sheet is the only settings surface the app has,
/// and game audio without an off switch is not acceptable.
class _SoundToggle extends StatelessWidget {
  const _SoundToggle({required this.appState});

  final AppState appState;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: SwitchListTile(
        key: const Key('profile-sound-toggle'),
        title: const Text('Sound'),
        subtitle: const Text('Cues when a job resolves'),
        value: appState.soundEnabled,
        onChanged: appState.setSoundEnabled,
      ),
    );
  }
}

/// An honest placeholder for everything the design reference shows that this app does not model
/// yet: display name, rank, respect, avatar/plate picker, achievements.
class _ComingSoonPanel extends StatelessWidget {
  const _ComingSoonPanel();

  @override
  Widget build(BuildContext context) {
    return Card(
      key: const Key('profile-coming-soon'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Text(
          'Rank, respect, achievements and a custom look are coming in a future update.',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodyMedium,
        ),
      ),
    );
  }
}
