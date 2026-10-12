import 'package:flutter/foundation.dart';

import '../api/models.dart';

enum NudgeKind { energyFull, propertyCapped }

/// One notification worth scheduling: what it says and the moment it should arrive.
///
/// [key] is stable for the thing being announced ('energy', 'property:street-corner'), so a
/// reschedule replaces the earlier nudge for that thing instead of stacking a second one beside it.
@immutable
class ScheduledNudge {
  const ScheduledNudge({
    required this.kind,
    required this.key,
    required this.at,
    required this.title,
    required this.body,
  });

  final NudgeKind kind;
  final String key;
  final DateTime at;
  final String title;
  final String body;

  @override
  bool operator ==(Object other) =>
      other is ScheduledNudge &&
      other.kind == kind &&
      other.key == key &&
      other.at == at &&
      other.title == title &&
      other.body == body;

  @override
  int get hashCode => Object.hash(kind, key, at, title, body);

  @override
  String toString() => 'ScheduledNudge($key at $at)';
}

/// What is worth announcing, given what the server last said.
///
/// Future state here is a pure function of a snapshot plus elapsed time, so the device can predict
/// the moment a meter fills or a property tops out without asking anyone. Each snapshot carries the
/// time it was served ([meFetchedAt], [propertiesFetchedAt]); the wait it reports runs from there.
///
/// Only a *specific thing the player owns* earns a nudge, and only while it is still to come: a
/// meter that is already full, a property already capped, or a moment already past says nothing.
/// [enabled] false (the player opted out, or never opted in) says nothing at all.
///
/// Sorted by time, earliest first.
List<ScheduledNudge> planReturnNudges({
  required bool enabled,
  required Me me,
  required DateTime meFetchedAt,
  required List<Property> properties,
  required DateTime propertiesFetchedAt,
  required DateTime now,
}) {
  if (!enabled) return const [];

  final nudges = <ScheduledNudge>[];

  final untilFull = me.energy.secondsUntilFull;
  if (untilFull != null) {
    nudges.add(ScheduledNudge(
      kind: NudgeKind.energyFull,
      key: 'energy',
      at: meFetchedAt.add(Duration(seconds: untilFull)),
      title: 'Energy is full',
      body: 'Your energy has refilled. Time to work.',
    ));
  }

  for (final property in properties) {
    final untilCap = property.secondsUntilCap;
    if (untilCap == null) continue;
    nudges.add(ScheduledNudge(
      kind: NudgeKind.propertyCapped,
      key: 'property:${property.id}',
      at: propertiesFetchedAt.add(Duration(seconds: untilCap)),
      title: '${property.name} is full',
      body: 'Collect now. It stops earning until you do.',
    ));
  }

  return nudges.where((nudge) => nudge.at.isAfter(now)).toList()
    ..sort((a, b) => a.at.compareTo(b.at));
}
