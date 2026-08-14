import 'package:flutter/foundation.dart';

@immutable
class MeterState {
  const MeterState({
    required this.current,
    required this.max,
    required this.regenSecondsPerPoint,
    this.secondsUntilNextPoint,
  });

  final int current;
  final int max;
  final int regenSecondsPerPoint;
  final int? secondsUntilNextPoint;

  factory MeterState.fromJson(Map<String, dynamic> json) => MeterState(
        current: json['current'] as int,
        max: json['max'] as int,
        regenSecondsPerPoint: json['regenSecondsPerPoint'] as int,
        secondsUntilNextPoint: json['secondsUntilNextPoint'] as int?,
      );
}

@immutable
class Me {
  const Me({
    required this.playerId,
    required this.energy,
    required this.stamina,
    required this.health,
    required this.cash,
    required this.premiumCurrency,
    required this.level,
    required this.xp,
    required this.skillPoints,
    required this.attack,
    required this.defense,
  });

  final String playerId;
  final MeterState energy;
  final MeterState stamina;
  final MeterState health;
  final int cash;
  final int premiumCurrency;
  final int level;
  final int xp;
  final int skillPoints;
  final int attack;
  final int defense;

  factory Me.fromJson(Map<String, dynamic> json) => Me(
        playerId: json['playerId'] as String,
        energy: MeterState.fromJson(json['energy'] as Map<String, dynamic>),
        stamina: MeterState.fromJson(json['stamina'] as Map<String, dynamic>),
        health: MeterState.fromJson(json['health'] as Map<String, dynamic>),
        cash: json['cash'] as int,
        premiumCurrency: json['premiumCurrency'] as int,
        level: json['level'] as int,
        xp: json['xp'] as int,
        skillPoints: json['skillPoints'] as int,
        attack: json['attack'] as int,
        defense: json['defense'] as int,
      );
}

@immutable
class SessionResult {
  const SessionResult({
    required this.deviceToken,
    required this.playerId,
  });

  final String deviceToken;
  final String playerId;

  factory SessionResult.fromJson(Map<String, dynamic> json) => SessionResult(
        deviceToken: json['deviceToken'] as String,
        playerId: json['playerId'] as String,
      );
}

@immutable
class ContentPack {
  const ContentPack({
    required this.name,
    required this.cashLabel,
    required this.premiumLabel,
    required this.primaryColorHex,
  });

  final String name;
  final String cashLabel;
  final String premiumLabel;
  final String primaryColorHex;

  factory ContentPack.fromJson(Map<String, dynamic> json) {
    final currencies = json['currencies'] as Map<String, dynamic>? ?? {};
    final cash = currencies['cash'] as Map<String, dynamic>? ?? {};
    final premium = currencies['premium'] as Map<String, dynamic>? ?? {};
    final palette = json['palette'] as Map<String, dynamic>? ?? {};
    return ContentPack(
      name: json['name'] as String? ?? '',
      cashLabel: cash['label'] as String? ?? 'Cash',
      premiumLabel: premium['label'] as String? ?? 'Gems',
      primaryColorHex: palette['primary'] as String? ?? '#6200EE',
    );
  }
}
