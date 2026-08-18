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

@immutable
class JobMastery {
  const JobMastery({
    required this.completions,
    required this.rank,
    required this.maxRank,
    this.completionsUntilNextRank,
  });

  final int completions;
  final int rank;
  final int maxRank;

  /// Null once [maxRank] is reached.
  final int? completionsUntilNextRank;

  /// Completions required per rank, derived from what the server sent.
  ///
  /// The server does not send the threshold, but it is exactly recoverable: the next rank boundary
  /// is `(rank + 1) * perRank`, and `completionsUntilNextRank` is that boundary minus [completions].
  /// So `perRank = (completions + remaining) / (rank + 1)` — an exact integer division, since the
  /// left-hand side is that product by definition. Null at max rank, where there is no next
  /// boundary to derive from.
  int? get completionsPerRank {
    final remaining = completionsUntilNextRank;
    if (remaining == null) return null;
    return (completions + remaining) ~/ (rank + 1);
  }

  /// Progress through the current rank, 0..1, for the mastery bar.
  ///
  /// Returns 1.0 at max rank so a maxed job reads as complete rather than empty, and 0.0 when the
  /// pack disables mastery entirely ([maxRank] of 0), where no bar should be shown at all.
  double get rankProgress {
    if (maxRank <= 0) return 0.0;

    final perRank = completionsPerRank;
    if (perRank == null) return 1.0; // at max rank
    if (perRank <= 0) return 0.0;

    final into = completions - (rank * perRank);
    return (into / perRank).clamp(0.0, 1.0);
  }

  /// Whether this job is fully mastered.
  bool get isMaxRank => maxRank > 0 && rank >= maxRank;

  factory JobMastery.fromJson(Map<String, dynamic> json) => JobMastery(
        completions: json['completions'] as int,
        rank: json['rank'] as int,
        maxRank: json['maxRank'] as int,
        completionsUntilNextRank: json['completionsUntilNextRank'] as int?,
      );
}

@immutable
class Job {
  const Job({
    required this.id,
    required this.name,
    required this.description,
    required this.area,
    required this.energyCost,
    required this.levelRequirement,
    required this.cashMin,
    required this.cashMax,
    required this.xp,
    required this.mastery,
    required this.canAttempt,
    this.unavailableReason,
  });

  final String id;
  final String name;
  final String description;
  final String area;
  final int energyCost;
  final int levelRequirement;
  final int cashMin;
  final int cashMax;
  final int xp;
  final JobMastery mastery;
  final bool canAttempt;

  /// Stable token from the server: 'level' or 'energy'. Null when [canAttempt] is true. The
  /// user-facing wording is the client's to choose.
  final String? unavailableReason;

  factory Job.fromJson(Map<String, dynamic> json) => Job(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        area: json['area'] as String? ?? '',
        energyCost: json['energyCost'] as int,
        levelRequirement: json['levelRequirement'] as int,
        cashMin: json['cashMin'] as int,
        cashMax: json['cashMax'] as int,
        xp: json['xp'] as int,
        mastery: JobMastery.fromJson(json['mastery'] as Map<String, dynamic>),
        canAttempt: json['canAttempt'] as bool,
        unavailableReason: json['unavailableReason'] as String?,
      );
}

@immutable
class JobList {
  const JobList({
    required this.jobs,
    required this.currentEnergy,
    required this.level,
  });

  final List<Job> jobs;
  final int currentEnergy;
  final int level;

  factory JobList.fromJson(Map<String, dynamic> json) => JobList(
        jobs: (json['jobs'] as List<dynamic>)
            .map((j) => Job.fromJson(j as Map<String, dynamic>))
            .toList(growable: false),
        currentEnergy: json['currentEnergy'] as int,
        level: json['level'] as int,
      );
}

@immutable
class LevelUp {
  const LevelUp({
    required this.levelsGained,
    required this.newLevel,
    required this.skillPointsGranted,
    required this.metersRefilled,
  });

  final int levelsGained;
  final int newLevel;
  final int skillPointsGranted;
  final bool metersRefilled;

  factory LevelUp.fromJson(Map<String, dynamic> json) => LevelUp(
        levelsGained: json['levelsGained'] as int,
        newLevel: json['newLevel'] as int,
        skillPointsGranted: json['skillPointsGranted'] as int,
        metersRefilled: json['metersRefilled'] as bool,
      );
}

@immutable
class JobResult {
  const JobResult({
    required this.jobId,
    required this.energySpent,
    required this.cashEarned,
    required this.xpEarned,
    required this.mastery,
    required this.masteryRankedUp,
  });

  final String jobId;
  final int energySpent;
  final int cashEarned;
  final int xpEarned;
  final JobMastery mastery;
  final bool masteryRankedUp;

  factory JobResult.fromJson(Map<String, dynamic> json) => JobResult(
        jobId: json['jobId'] as String,
        energySpent: json['energySpent'] as int,
        cashEarned: json['cashEarned'] as int,
        xpEarned: json['xpEarned'] as int,
        mastery: JobMastery.fromJson(json['mastery'] as Map<String, dynamic>),
        masteryRankedUp: json['masteryRankedUp'] as bool,
      );
}

/// Response to a do-job call. Carries the full post-action player state, so the client never needs
/// a follow-up /me after doing a job.
@immutable
class DoJobResult {
  const DoJobResult({
    required this.result,
    required this.player,
    this.levelUp,
  });

  final JobResult result;
  final Me player;
  final LevelUp? levelUp;

  factory DoJobResult.fromJson(Map<String, dynamic> json) => DoJobResult(
        result: JobResult.fromJson(json['result'] as Map<String, dynamic>),
        player: Me.fromJson(json['player'] as Map<String, dynamic>),
        levelUp: json['levelUp'] == null
            ? null
            : LevelUp.fromJson(json['levelUp'] as Map<String, dynamic>),
      );
}
