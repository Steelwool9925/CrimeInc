import 'package:flutter/material.dart';

/// A faint dot-grid texture standing in for the comic-noir print halftone -- meant to sit behind
/// content as atmosphere. [opacity] defaults low enough that removing this widget entirely would
/// change nothing about what's readable; it never carries contrast or meaning on its own.
///
/// Wrap it in a [Positioned.fill] (in a [Stack]) or give it an explicit size -- it fills whatever
/// box it's given and ignores pointer events, so it never steals a tap meant for the content
/// drawn on top of it.
class HalftoneWash extends StatelessWidget {
  const HalftoneWash({
    super.key,
    required this.color,
    this.opacity = 0.12,
    this.dotSpacing = 7,
    this.dotRadius = 1.0,
  });

  final Color color;
  final double opacity;
  final double dotSpacing;
  final double dotRadius;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: CustomPaint(
        size: Size.infinite,
        painter: _HalftonePainter(
          color: color.withValues(alpha: opacity),
          spacing: dotSpacing,
          radius: dotRadius,
        ),
      ),
    );
  }
}

class _HalftonePainter extends CustomPainter {
  _HalftonePainter({required this.color, required this.spacing, required this.radius});

  final Color color;
  final double spacing;
  final double radius;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()..color = color;
    for (double y = spacing / 2; y < size.height; y += spacing) {
      for (double x = spacing / 2; x < size.width; x += spacing) {
        canvas.drawCircle(Offset(x, y), radius, paint);
      }
    }
  }

  @override
  bool shouldRepaint(covariant _HalftonePainter oldDelegate) =>
      oldDelegate.color != color ||
      oldDelegate.spacing != spacing ||
      oldDelegate.radius != radius;
}
