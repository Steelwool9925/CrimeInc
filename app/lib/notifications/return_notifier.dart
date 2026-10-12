import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:timezone/timezone.dart' as tz;

import 'return_schedule.dart';

/// The platform half of return notifications: asking permission and putting [ScheduledNudge]s on
/// the device. What is worth saying, and when, is decided by [planReturnNudges]; this only carries
/// it out.
///
/// Every method is safe to call on any platform and never throws. A notification that fails to
/// schedule costs the player a nudge, never the app.
abstract class ReturnNotifier {
  /// Whether this device can show scheduled notifications at all. False on web and desktop, which
  /// the app does not target for them; the UI hides the setting and the first-job request is
  /// skipped, so a player there is never asked for something that cannot work.
  bool get isSupported;

  /// Asks the OS for permission to notify. True when it is held afterwards. May show the system
  /// prompt, which on Android 13+ is effectively spendable once.
  Future<bool> requestPermission();

  /// Replaces everything scheduled with [nudges]. An empty list clears it.
  Future<void> replaceAll(List<ScheduledNudge> nudges);

  /// Removes everything scheduled, and anything already delivered to the tray.
  Future<void> cancelAll();
}

/// Does nothing. The default, so a test or an unsupported platform schedules nothing and asks for
/// nothing.
class SilentReturnNotifier implements ReturnNotifier {
  const SilentReturnNotifier();

  @override
  bool get isSupported => false;

  @override
  Future<bool> requestPermission() async => false;

  @override
  Future<void> replaceAll(List<ScheduledNudge> nudges) async {}

  @override
  Future<void> cancelAll() async {}
}

/// A stable positive id for [key], so a nudge scheduled twice replaces itself rather than
/// stacking. FNV-1a, folded into the 31 bits the plugin accepts.
@visibleForTesting
int nudgeId(String key) {
  var hash = 0x811c9dc5;
  for (final unit in key.codeUnits) {
    hash ^= unit;
    hash = (hash * 0x01000193) & 0xffffffff;
  }
  return hash & 0x7fffffff;
}

/// Android local notifications through `flutter_local_notifications`.
///
/// Scheduling is inexact on purpose: an exact alarm needs a permission Android 14 denies by
/// default, to be a few minutes more punctual about "your energy is full".
class LocalReturnNotifier implements ReturnNotifier {
  LocalReturnNotifier({FlutterLocalNotificationsPlugin? plugin})
      : _plugin = plugin ?? FlutterLocalNotificationsPlugin();

  final FlutterLocalNotificationsPlugin _plugin;
  bool _initialized = false;

  static const _channel = AndroidNotificationDetails(
    'return_nudges',
    'Return reminders',
    channelDescription: 'Tells you when your energy is full or a property is ready to collect.',
  );

  @override
  bool get isSupported => !kIsWeb && defaultTargetPlatform == TargetPlatform.android;

  Future<AndroidFlutterLocalNotificationsPlugin?> _android() async {
    if (!isSupported) return null;
    if (!_initialized) {
      await _plugin.initialize(
        settings: const InitializationSettings(
          android: AndroidInitializationSettings('@mipmap/ic_launcher'),
        ),
      );
      _initialized = true;
    }
    return _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
  }

  @override
  Future<bool> requestPermission() async {
    try {
      final android = await _android();
      return await android?.requestNotificationsPermission() ?? false;
    } catch (e) {
      debugPrint('Return notifications: permission request failed: $e');
      return false;
    }
  }

  @override
  Future<void> replaceAll(List<ScheduledNudge> nudges) async {
    try {
      final android = await _android();
      if (android == null) return;
      await android.cancelAll();
      for (final nudge in nudges) {
        try {
          await android.zonedSchedule(
            id: nudgeId(nudge.key),
            title: nudge.title,
            body: nudge.body,
            scheduledDate: tz.TZDateTime.from(nudge.at, tz.UTC),
            notificationDetails: _channel,
            scheduleMode: AndroidScheduleMode.inexactAllowWhileIdle,
          );
        } catch (e) {
          debugPrint('Return notifications: could not schedule ${nudge.key}: $e');
        }
      }
    } catch (e) {
      debugPrint('Return notifications: could not replace the schedule: $e');
    }
  }

  @override
  Future<void> cancelAll() async {
    try {
      await (await _android())?.cancelAll();
    } catch (e) {
      debugPrint('Return notifications: could not cancel: $e');
    }
  }
}
