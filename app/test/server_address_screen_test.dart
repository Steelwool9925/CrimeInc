import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/screens/server_address_screen.dart';

void main() {
  group('isPrivateOrLocalHost', () {
    test('localhost and loopback are local', () {
      expect(isPrivateOrLocalHost('localhost'), isTrue);
      expect(isPrivateOrLocalHost('127.0.0.1'), isTrue);
    });

    test('private IPv4 ranges are local', () {
      expect(isPrivateOrLocalHost('10.0.0.5'), isTrue);
      expect(isPrivateOrLocalHost('172.16.0.1'), isTrue);
      expect(isPrivateOrLocalHost('172.31.255.255'), isTrue);
      expect(isPrivateOrLocalHost('192.168.1.1'), isTrue);
    });

    test('a public IPv4 address is not local', () {
      expect(isPrivateOrLocalHost('8.8.8.8'), isFalse);
      // Just outside the 172.16.0.0/12 range on either side.
      expect(isPrivateOrLocalHost('172.15.255.255'), isFalse);
      expect(isPrivateOrLocalHost('172.32.0.0'), isFalse);
    });

    test('a public hostname is not local', () {
      expect(isPrivateOrLocalHost('your-server.example.com'), isFalse);
    });
  });

  group('isAcceptableServerAddress', () {
    test('a public https address is acceptable', () {
      expect(isAcceptableServerAddress('https://your-server.example.com'), isTrue);
    });

    test('a public http address is refused', () {
      expect(isAcceptableServerAddress('http://your-server.example.com'), isFalse);
    });

    test('a local http address is acceptable', () {
      expect(isAcceptableServerAddress('http://localhost:5080'), isTrue);
      expect(isAcceptableServerAddress('http://192.168.1.50:5080'), isTrue);
    });

    test('a local https address is also acceptable', () {
      expect(isAcceptableServerAddress('https://localhost:5080'), isTrue);
    });

    test('empty, blank, or garbage input is refused', () {
      expect(isAcceptableServerAddress(''), isFalse);
      expect(isAcceptableServerAddress('   '), isFalse);
      expect(isAcceptableServerAddress('not a url'), isFalse);
    });

    test('a non-http(s) scheme is refused', () {
      expect(isAcceptableServerAddress('ftp://your-server.example.com'), isFalse);
    });
  });

  group('ServerAddressScreen', () {
    Future<void> pump(WidgetTester tester, Future<void> Function(String) onSaved) =>
        tester.pumpWidget(
          MaterialApp(home: ServerAddressScreen(onSaved: onSaved)),
        );

    testWidgets('a valid https address calls onSaved with the trimmed value', (tester) async {
      String? saved;
      await pump(tester, (address) async {
        saved = address;
      });

      await tester.enterText(
        find.byKey(const Key('server-address-field')),
        '  https://your-server.example.com  ',
      );
      await tester.tap(find.byKey(const Key('server-address-save')));
      // Not pumpAndSettle: _save's spinner (CircularProgressIndicator) animates indefinitely once
      // _saving is true, and this test double's onSaved never replaces the widget the way
      // main.dart's real callback does — pumpAndSettle would wait for the spinner forever. Two
      // bounded pumps are enough to run the awaited onSaved call to completion.
      await tester.pump();
      await tester.pump();

      expect(saved, 'https://your-server.example.com');
      expect(find.byKey(const Key('server-address-error')), findsNothing);
    });

    testWidgets('an insecure non-local address shows an error and does not call onSaved',
        (tester) async {
      var called = false;
      await pump(tester, (address) async {
        called = true;
      });

      await tester.enterText(
        find.byKey(const Key('server-address-field')),
        'http://your-server.example.com',
      );
      await tester.tap(find.byKey(const Key('server-address-save')));
      await tester.pump();

      expect(called, isFalse);
      expect(find.byKey(const Key('server-address-error')), findsOneWidget);
    });

    testWidgets('an empty address shows an error and does not call onSaved', (tester) async {
      var called = false;
      await pump(tester, (address) async {
        called = true;
      });

      await tester.tap(find.byKey(const Key('server-address-save')));
      await tester.pump();

      expect(called, isFalse);
      expect(find.byKey(const Key('server-address-error')), findsOneWidget);
      expect(find.text('Enter a server address.'), findsOneWidget);
    });

    testWidgets('a local http address is accepted', (tester) async {
      String? saved;
      await pump(tester, (address) async {
        saved = address;
      });

      await tester.enterText(
        find.byKey(const Key('server-address-field')),
        'http://192.168.1.50:5080',
      );
      await tester.tap(find.byKey(const Key('server-address-save')));
      // See the note in the https test above — bounded pumps, not pumpAndSettle.
      await tester.pump();
      await tester.pump();

      expect(saved, 'http://192.168.1.50:5080');
    });
  });
}
