import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/notifications/return_schedule.dart';

import 'fakes.dart';

final _t0 = DateTime.utc(2026, 10, 8, 12);

Me _meWithEnergy(MeterState energy) => Me(
      playerId: kFakeMe.playerId,
      energy: energy,
      stamina: kFakeMe.stamina,
      health: kFakeMe.health,
      cash: kFakeMe.cash,
      premiumCurrency: kFakeMe.premiumCurrency,
      level: kFakeMe.level,
      xp: kFakeMe.xp,
      skillPoints: kFakeMe.skillPoints,
      attack: kFakeMe.attack,
      defense: kFakeMe.defense,
    );

Property _property({
  String id = 'street-corner',
  int incomePerHour = 240,
  int quantity = 1,
  int pending = 30,
  int cap = 60,
}) =>
    Property(
      id: id,
      name: 'Street Corner',
      description: '',
      levelRequirement: 1,
      price: 500,
      incomePerHour: incomePerHour,
      accrualCapMinutes: 15,
      quantity: quantity,
      pending: pending,
      cap: cap,
      canBuy: true,
      canCollect: true,
    );

const _fullEnergy = MeterState(current: 10, max: 10, regenSecondsPerPoint: 300);
const _threeShort = MeterState(
  current: 7,
  max: 10,
  regenSecondsPerPoint: 300,
  secondsUntilNextPoint: 42,
);

List<ScheduledNudge> _plan({
  bool enabled = true,
  MeterState energy = _fullEnergy,
  List<Property> properties = const [],
  DateTime? now,
  DateTime? meFetchedAt,
  DateTime? propertiesFetchedAt,
}) =>
    planReturnNudges(
      enabled: enabled,
      me: _meWithEnergy(energy),
      meFetchedAt: meFetchedAt ?? _t0,
      properties: properties,
      propertiesFetchedAt: propertiesFetchedAt ?? _t0,
      now: now ?? _t0,
    );

void main() {
  group('MeterState.secondsUntilFull', () {
    test('is null when the meter is already full', () {
      expect(_fullEnergy.secondsUntilFull, isNull);
    });

    test('is the wait for the next point plus a whole regen interval for each point after it', () {
      expect(_threeShort.secondsUntilFull, 42 + 2 * 300);
    });

    test('is just the wait for the next point when one point is missing', () {
      const oneShort = MeterState(
        current: 9,
        max: 10,
        regenSecondsPerPoint: 300,
        secondsUntilNextPoint: 17,
      );
      expect(oneShort.secondsUntilFull, 17);
    });

    test('falls back to a whole interval when the server omitted the wait', () {
      const noWait = MeterState(current: 9, max: 10, regenSecondsPerPoint: 300);
      expect(noWait.secondsUntilFull, 300);
    });
  });

  group('Property.secondsUntilCap', () {
    test('rounds up, so the projection reads the cap at that moment and not a second before', () {
      for (final property in [
        _property(),
        _property(pending: 0, cap: 600, incomePerHour: 300),
        _property(pending: 1, cap: 60, incomePerHour: 7),
        _property(pending: 59, cap: 60, incomePerHour: 240, quantity: 3),
      ]) {
        final seconds = property.secondsUntilCap!;
        expect(property.projectedPending(seconds), property.cap,
            reason: 'at $seconds s the counter should be full');
        expect(property.projectedPending(seconds - 1), lessThan(property.cap),
            reason: 'one second earlier it should not be');
      }
    });

    test('scales with the units owned', () {
      final one = _property(pending: 0, cap: 60, quantity: 1).secondsUntilCap!;
      final two = _property(pending: 0, cap: 120, quantity: 2).secondsUntilCap!;
      expect(two, one);
    });

    test('is null when nothing is filling', () {
      expect(_property(quantity: 0, cap: 0, pending: 0).secondsUntilCap, isNull);
      expect(_property(pending: 60, cap: 60).secondsUntilCap, isNull);
      expect(_property(incomePerHour: 0).secondsUntilCap, isNull);
    });
  });

  group('planReturnNudges', () {
    test('says nothing when the player has opted out', () {
      expect(
        _plan(enabled: false, energy: _threeShort, properties: [_property()]),
        isEmpty,
      );
    });

    test('schedules energy at the moment the bar fills', () {
      final nudges = _plan(energy: _threeShort);

      expect(nudges, hasLength(1));
      expect(nudges.single.kind, NudgeKind.energyFull);
      expect(nudges.single.at, _t0.add(const Duration(seconds: 642)));
    });

    test('says nothing about energy that is already full', () {
      expect(_plan(energy: _fullEnergy), isEmpty);
    });

    test('schedules an owned property at the moment it caps', () {
      final nudges = _plan(properties: [_property()]);

      expect(nudges, hasLength(1));
      expect(nudges.single.kind, NudgeKind.propertyCapped);
      expect(nudges.single.key, 'property:street-corner');
      expect(nudges.single.at, _t0.add(const Duration(seconds: 450)));
      expect(nudges.single.title, contains('Street Corner'));
    });

    test('says nothing about a property that is already capped, unowned, or earning nothing', () {
      expect(
        _plan(properties: [
          _property(pending: 60, cap: 60),
          _property(id: 'unowned', quantity: 0, pending: 0, cap: 0),
          _property(id: 'dud', incomePerHour: 0),
        ]),
        isEmpty,
      );
    });

    test('measures each snapshot from the time it was served', () {
      final meAt = _t0.subtract(const Duration(minutes: 3));
      final propertiesAt = _t0.subtract(const Duration(minutes: 1));

      final nudges = _plan(
        energy: _threeShort,
        properties: [_property()],
        meFetchedAt: meAt,
        propertiesFetchedAt: propertiesAt,
      );

      expect(
        nudges.firstWhere((n) => n.kind == NudgeKind.energyFull).at,
        meAt.add(const Duration(seconds: 642)),
      );
      expect(
        nudges.firstWhere((n) => n.kind == NudgeKind.propertyCapped).at,
        propertiesAt.add(const Duration(seconds: 450)),
      );
    });

    test('never schedules a moment that has already passed', () {
      final later = _t0.add(const Duration(seconds: 500));

      final nudges = _plan(
        energy: _threeShort,
        properties: [_property()],
        now: later,
      );

      expect(nudges.map((n) => n.kind), [NudgeKind.energyFull]);
      expect(nudges.every((n) => n.at.isAfter(later)), isTrue);
    });

    test('treats a moment that is exactly now as already passed', () {
      final nudges = _plan(
        properties: [_property()],
        now: _t0.add(const Duration(seconds: 450)),
      );

      expect(nudges, isEmpty);
    });

    test('returns the earliest first, one nudge for each thing, each with its own key', () {
      final nudges = _plan(
        energy: _threeShort,
        properties: [
          _property(id: 'late', pending: 0),
          _property(id: 'soon', pending: 50),
        ],
      );

      expect(nudges.map((n) => n.key), ['property:soon', 'energy', 'property:late']);
      expect(nudges.map((n) => n.key).toSet(), hasLength(nudges.length));
    });

    test('plans the same nudges from the same snapshot, so a reschedule replaces rather than adds', () {
      final first = _plan(energy: _threeShort, properties: [_property()]);
      final second = _plan(energy: _threeShort, properties: [_property()]);

      expect(second, first);
    });
  });
}
