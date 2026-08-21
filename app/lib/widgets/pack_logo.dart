import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';

/// The active pack's logo, fetched from the manifest-driven asset route (T4.1).
///
/// Renders nothing but the pack name when the manifest declares no logo, and degrades to the
/// same text if the image fails to load. A content pack is data supplied from outside the app:
/// missing or broken art must look plain, never like a crash.
class PackLogo extends StatelessWidget {
  const PackLogo({
    super.key,
    required this.appState,
    required this.pack,
    this.height = 44,
  });

  /// Key on the [Image] itself, so tests can assert the URL requested without rendering it.
  static const Key imageKey = Key('pack-logo');

  final AppState appState;
  final ContentPack? pack;
  final double height;

  @override
  Widget build(BuildContext context) {
    final name = pack?.name ?? '';

    if (pack == null || !pack!.hasAsset('logo')) {
      return _name(context, name);
    }

    return Image.network(
      appState.assetUrl('logo'),
      key: imageKey,
      height: height,
      fit: BoxFit.contain,
      // Never surface a broken-image glyph in the app bar; the name reads fine on its own.
      errorBuilder: (context, _, __) => _name(context, name),
      // Hold the final height while loading so the bar does not jump when the art arrives.
      frameBuilder: (context, child, frame, wasSyncLoaded) =>
          frame == null && !wasSyncLoaded ? SizedBox(height: height) : child,
    );
  }

  Widget _name(BuildContext context, String name) => Text(
        name,
        style: Theme.of(context).textTheme.titleLarge?.copyWith(
              color: Theme.of(context).appBarTheme.foregroundColor,
              fontWeight: FontWeight.w600,
            ),
      );
}
