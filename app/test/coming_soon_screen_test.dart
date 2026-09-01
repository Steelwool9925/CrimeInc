import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/widgets/coming_soon_screen.dart';

Future<void> pumpComingSoon(
  WidgetTester tester, {
  required String title,
  required IconData icon,
  required String message,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: ComingSoonScreen(title: title, icon: icon, message: message),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('ComingSoonScreen — display', () {
    testWidgets('shows the title in the AppBar, the message, and the icon', (tester) async {
      await pumpComingSoon(
        tester,
        title: 'Crew',
        icon: Icons.groups,
        message: 'Crew management is not live yet.',
      );

      expect(find.widgetWithText(AppBar, 'Crew'), findsOneWidget);
      expect(find.byKey(const Key('coming-soon-message')), findsOneWidget);
      expect(find.text('Crew management is not live yet.'), findsOneWidget);
      expect(find.byKey(const Key('coming-soon-icon')), findsOneWidget);

      final icon = tester.widget<Icon>(find.byKey(const Key('coming-soon-icon')));
      expect(icon.icon, Icons.groups);
    });
  });

  group('ComingSoonScreen — reusability', () {
    testWidgets('a Crew configuration renders its own title and message', (tester) async {
      await pumpComingSoon(
        tester,
        title: 'Crew',
        icon: Icons.groups,
        message: 'Recruit and manage your crew here soon.',
      );

      expect(find.widgetWithText(AppBar, 'Crew'), findsOneWidget);
      expect(find.text('Recruit and manage your crew here soon.'), findsOneWidget);
      expect(find.text('Territory'), findsNothing);
    });

    testWidgets('a Territory configuration renders its own distinct title and message', (tester) async {
      await pumpComingSoon(
        tester,
        title: 'Territory',
        icon: Icons.map,
        message: 'Claim and defend turf here soon.',
      );

      expect(find.widgetWithText(AppBar, 'Territory'), findsOneWidget);
      expect(find.text('Claim and defend turf here soon.'), findsOneWidget);
      expect(find.text('Crew'), findsNothing);

      final icon = tester.widget<Icon>(find.byKey(const Key('coming-soon-icon')));
      expect(icon.icon, Icons.map);
    });
  });
}
