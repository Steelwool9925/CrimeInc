import 'package:flutter/material.dart';

/// A diagonal-stripe section divider -- the comic-noir "speed line" motif standing in for a plain
/// horizontal rule. Meant for major section breaks only (e.g. before the dashboard grid); used on
/// every divider it would just be noise instead of marking a scene break.
class SpeedlineDivider extends StatelessWidget {
  const SpeedlineDivider({super.key, required this.color, this.height = 12, this.opacity = 0.5});

  final Color color;
  final double height;
  final double opacity;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: height,
      width: double.infinity,
      child: CustomPaint(
        painter: _SpeedlinePainter(color: color.withValues(alpha: opacity)),
      ),
    );
  }
}

class _SpeedlinePainter extends CustomPainter {
  _SpeedlinePainter({required this.color});

  final Color color;

  // Horizontal run per diagonal line, and the gap between lines -- shallow enough to read as
  // motion rather than a zig-zag.
  static const double _run = 10;
  static const double _gap = 14;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2;

    // Lines run from bottom-left to top-right, offset across the whole width (plus one height's
    // worth of margin on each side so a line's top end is never clipped away before its bottom
    // end scrolls into the visible band).
    for (double x = -size.height; x < size.width + size.height; x += _gap) {
      canvas.drawLine(Offset(x, size.height), Offset(x + _run, 0), paint);
    }
  }

  @override
  bool shouldRepaint(covariant _SpeedlinePainter oldDelegate) => oldDelegate.color != color;
}
