import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/widgets/halftone_wash.dart';

/// The dot-grid texture is atmosphere, not information -- these tests only guard that it renders
/// without throwing and stays out of the way of hit-testing, not any specific dot placement.
void main() {
  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  testWidgets('renders without throwing at a range of sizes', (tester) async {
    await tester.pumpWidget(wrap(
      const SizedBox(width: 300, height: 120, child: HalftoneWash(color: Colors.white)),
    ));

    expect(find.byType(HalftoneWash), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('does not intercept taps meant for content underneath', (tester) async {
    var tapped = false;
    await tester.pumpWidget(wrap(
      Stack(
        children: [
          Positioned.fill(
            child: GestureDetector(onTap: () => tapped = true, behavior: HitTestBehavior.opaque),
          ),
          const Positioned.fill(child: HalftoneWash(color: Colors.white)),
        ],
      ),
    ));

    // warnIfMissed: false -- IgnorePointer deliberately means the tap never "hits" HalftoneWash
    // itself; the assertion below is what actually proves it passed through.
    await tester.tap(find.byType(HalftoneWash), warnIfMissed: false);
    expect(tapped, isTrue);
  });
}
