import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';

/// Builds an [http.Client] that falls back to DNS-over-HTTPS when the platform's own DNS
/// resolution fails for a request's hostname.
///
/// Some mobile carriers/networks filter DNS for hostnames that look machine-generated — exactly
/// what a quick tunnel's randomly-assigned wordmark hostname looks like (`trycloudflare.com`,
/// `ngrok-free.app`, etc.) — and some filter more broadly than that, breaking the platform
/// resolver for everything except an allowlist a browser's own bundled secure DNS never has to
/// consult. Browsers dodge both cases because most ship their own DNS-over-HTTPS; this gives the
/// app the same escape hatch. The fast path (a device with working DNS) is unchanged —
/// [Socket.startConnect] with the hostname does exactly what the default [HttpClient] would do,
/// and the DoH detour is only ever reached on an actual [SocketException] from that lookup.
///
/// Once [HttpClient.connectionFactory] is set at all, [HttpClient] stops doing TLS itself — that
/// only happens on its own default (no-factory) path. So for an `https://` [url] this function is
/// on the hook for the TLS handshake too, not just the TCP connect: [_openConnection] connects a
/// plain [Socket] first (to the resolved IP, if this is the DoH-fallback branch, or straight to
/// [url]'s hostname otherwise), then layers TLS on top via [SecureSocket.secure] — passing the
/// *original* hostname as `host` there regardless of which IP the socket actually dialed, so SNI
/// and certificate validation both still target the real name. That's also exactly why the DoH
/// fallback works for a tunnel at all: Cloudflare's (and most CDNs') edge routes purely on
/// SNI/Host, not on which of their IPs you happened to connect to.
http.Client createResilientHttpClient() {
  // Everything below is built on dart:io (HttpClient/Socket/SecureSocket), none of which exist on
  // the web platform — constructing HttpClient() there throws "Unsupported operation:
  // Platform._version" immediately, uncaught, the instant this function runs. The browser's own
  // fetch stack already does its own DNS resolution (and typically ships its own DoH), so the
  // carrier-DNS-filtering problem this file exists for doesn't apply to web in the first place —
  // it gets the plain client package:http already hands out there.
  if (kIsWeb) return http.Client();

  final client = HttpClient();
  client.connectionFactory = (Uri url, String? proxyHost, int? proxyPort) async {
    try {
      // Deliberately await .socket here, inside this try — Socket.startConnect's own Future
      // resolves as soon as the ConnectionTask exists, *before* DNS resolution/connect actually
      // happen; those run lazily inside .socket instead. Returning the task un-awaited (as this
      // used to) let a lookup failure surface later, inside HttpClient's own internals, past this
      // catch entirely — which is why the DoH fallback below was never reached.
      return await _openConnection(url, connectHost: url.host);
    } on SocketException catch (original) {
      final InternetAddress resolved;
      try {
        resolved = await _resolveViaDoh(url.host);
      } catch (dohError) {
        // Neither the platform resolver nor DoH could resolve this host — surface both reasons
        // together rather than just the platform one, since which providers were tried (and how
        // they failed) is exactly what tells apart "this network blocks everything, including
        // direct-IP HTTPS to 1.1.1.1/8.8.8.8" from "the tunnel really is down".
        throw SocketException(
          '${original.message} — DoH fallback also failed: $dohError',
          osError: original.osError,
        );
      }
      return await _openConnection(url, connectHost: resolved);
    }
  };
  return IOClient(client);
}

/// Connects to [connectHost]:[url.port] and, for an `https://` [url], layers TLS on top with
/// [url]'s hostname as the SNI/certificate name — decoupled from [connectHost] so this works
/// whether that's [url]'s own hostname (the fast path) or a DoH-resolved IP (the fallback).
Future<ConnectionTask<Socket>> _openConnection(Uri url, {required dynamic connectHost}) async {
  final task = await Socket.startConnect(connectHost, url.port);
  final rawSocket = await task.socket;
  if (!url.isScheme('https')) {
    return ConnectionTask.fromSocket(Future.value(rawSocket), task.cancel);
  }
  final secureSocket = await SecureSocket.secure(rawSocket, host: url.host);
  return ConnectionTask.fromSocket(Future.value(secureSocket), task.cancel);
}

/// Successfully DoH-resolved addresses, kept for the process lifetime so a broken platform
/// resolver costs one extra round trip per hostname rather than one per request — this app polls
/// several routes every 10s, and re-querying DoH on every single one would be wasteful and slow.
final Map<String, InternetAddress> _dohCache = {};

/// Resolves [hostname] via DNS-over-HTTPS, trying Cloudflare's endpoint then Google's as a second
/// opinion — both are long-established, ordinary-looking domains, unlikely to be caught by the
/// same kind of filter that blocks a quick tunnel's random hostname. Throws a [StateError] naming
/// why each provider failed if neither answers, rather than swallowing to a bare retry of the
/// original lookup failure — a silent "no DoH address" leaves no trail for whoever hits this next
/// to tell "both providers were unreachable" apart from "the fallback itself never ran".
Future<InternetAddress> _resolveViaDoh(String hostname) async {
  final cached = _dohCache[hostname];
  if (cached != null) return cached;

  // Bare IPs, not hostnames — resolving cloudflare-dns.com/dns.google would need DNS itself,
  // which is the exact thing that might be broken here. 1.1.1.1 and 8.8.8.8 are dedicated
  // resolver IPs whose TLS certificates carry the literal IP as a SAN specifically so DoH can be
  // bootstrapped with zero prior DNS resolution.
  final reasons = <String>[];
  for (final query in [
    Uri.https('1.1.1.1', '/dns-query', {'name': hostname, 'type': 'A'}),
    Uri.https('8.8.8.8', '/resolve', {'name': hostname, 'type': 'A'}),
  ]) {
    try {
      final resolved = await _tryDohQuery(query);
      _dohCache[hostname] = resolved;
      return resolved;
    } catch (e) {
      reasons.add('${query.host}: $e');
    }
  }
  throw StateError(reasons.join('; '));
}

Future<InternetAddress> _tryDohQuery(Uri query) async {
  // A plain, unmodified HttpClient — fine, since query is IP-literal and needs no DNS lookup at
  // all to connect. TLS still validates correctly: both providers' certificates carry the literal
  // IP as a SAN precisely so this bootstrap works with no prior resolution of any kind.
  final dohClient = HttpClient();
  try {
    final request = await dohClient.getUrl(query);
    request.headers.set('accept', 'application/dns-json');
    final response = await request.close().timeout(const Duration(seconds: 5));
    final body = await response.transform(utf8.decoder).join();
    final json = jsonDecode(body) as Map<String, dynamic>;
    final answers = json['Answer'] as List<dynamic>?;
    if (answers == null) throw StateError('no Answer section in response: $body');
    for (final answer in answers) {
      final data = (answer as Map<String, dynamic>)['data'] as String?;
      final address = data == null ? null : InternetAddress.tryParse(data);
      if (address != null) return address;
    }
    throw StateError('Answer section had no parseable address: $body');
  } finally {
    dohClient.close();
  }
}
