import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/widgets/speedline_divider.dart';

void main() {
  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  testWidgets('renders without throwing and reports its given height', (tester) async {
    await tester.pumpWidget(wrap(const SpeedlineDivider(color: Colors.white, height: 16)));

    expect(find.byType(SpeedlineDivider), findsOneWidget);
    expect(tester.getSize(find.byType(SpeedlineDivider)).height, 16);
    expect(tester.takeException(), isNull);
  });

  testWidgets('spans the full width available to it', (tester) async {
    await tester.pumpWidget(wrap(const SpeedlineDivider(color: Colors.white)));

    final width = tester.getSize(find.byType(SpeedlineDivider)).width;
    // 800 is flutter_test's default surface width.
    expect(width, 800);
  });
}
