import 'package:flutter/material.dart';

/// Caps how wide the app's content may grow, and centres it in whatever space is left over.
///
/// This is a mobile-shaped game that also runs in a desktop browser. Without a cap, every screen
/// stretches to the full window: on a 1568px-wide monitor the job cards and the single
/// server-address input ran edge to edge, and Home's 2-column tile grid — whose tiles are sized by
/// aspect ratio — produced tiles roughly 760px wide and 585px tall, each holding one small icon
/// and a word. One row of navigation filled the viewport and pushed the rest below the fold.
///
/// Capping the width fixes both at once, and keeps running text near a readable measure rather
/// than spanning the window.
///
/// Applied through `MaterialApp.builder` so it covers **every** route, including ones pushed with
/// `Navigator.push` (Market, Inventory, Skills) that never pass through `AppShell`.
class ContentWidthLimit extends StatelessWidget {
  const ContentWidthLimit({super.key, required this.child});

  /// Wide enough that a phone or tablet is never letterboxed — below this the widget is a no-op —
  /// and narrow enough that a desktop window reads as a deliberately centred app rather than a
  /// stretched one.
  static const double maxContentWidth = 1100;

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      // The area beside the content is painted in the same surface as the app itself, so the cap
      // reads as margin rather than as a letterbox of some other colour.
      color: Theme.of(context).colorScheme.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: maxContentWidth),
          child: child,
        ),
      ),
    );
  }
}
