import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/widgets/job_drama.dart';

import 'fakes.dart';

/// The real crime pack's jobs, so the thresholds are pinned against the numbers a player sees rather
/// than against figures invented to fit them. `flutter test` runs from `app/`.
List<Job> realJobs() {
  final decoded = jsonDecode(File('../content/crime/jobs.json').readAsStringSync());
  final rows = (decoded is Map ? decoded['jobs'] : decoded) as List<dynamic>;
  return [
    for (final row in rows.cast<Map<String, dynamic>>())
      Job(
        id: row['id'] as String,
        name: row['name'] as String,
        description: '',
        area: '',
        energyCost: row['energyCost'] as int,
        levelRequirement: row['levelRequirement'] as int,
        cashMin: (row['payout'] as Map<String, dynamic>)['cashMin'] as int,
        cashMax: (row['payout'] as Map<String, dynamic>)['cashMax'] as int,
        xp: (row['payout'] as Map<String, dynamic>)['xp'] as int,
        attackRequirement: 0,
        defenseRequirement: 0,
        successChance: ((row['risk'] as Map<String, dynamic>)['baseSuccessChance'] as num).toDouble(),
        mastery: kFakeMastery,
        canAttempt: true,
      ),
  ];
}

Job syntheticJob({int cashMin = 100, int cashMax = 200, double successChance = 0.9}) => Job(
      id: 'synthetic',
      name: 'Synthetic',
      description: '',
      area: '',
      energyCost: 1,
      levelRequirement: 1,
      cashMin: cashMin,
      cashMax: cashMax,
      xp: 10,
      attackRequirement: 0,
      defenseRequirement: 0,
      successChance: successChance,
      mastery: kFakeMastery,
      canAttempt: true,
    );

JobResult win(int cash, {bool rankedUp = false}) => JobResult(
      jobId: 'synthetic',
      succeeded: true,
      energySpent: 1,
      cashEarned: cash,
      xpEarned: 10,
      healthLost: 0,
      mastery: kFakeMastery,
      masteryRankedUp: rankedUp,
    );

JobResult loss(int cash) => JobResult(
      jobId: 'synthetic',
      succeeded: false,
      energySpent: 1,
      cashEarned: cash,
      xpEarned: 1,
      healthLost: 3,
      mastery: kFakeMastery,
      masteryRankedUp: false,
    );

void main() {
  group('JobDrama against the real pack', () {
    final jobs = realJobs();

    test('the pack has jobs to pin against', () {
      expect(jobs.length, greaterThanOrEqualTo(6));
    });

    test('a minimum roll is a scrape on every job', () {
      for (final job in jobs) {
        expect(JobDrama.of(win(job.cashMin), job), JobDrama.clean, reason: job.id);
      }
    });

    test('a maximum roll is a major win on every job', () {
      for (final job in jobs) {
        expect(JobDrama.of(win(job.cashMax), job), JobDrama.major, reason: job.id);
      }
    });

    test('a roll halfway up the range is a strong win on every job', () {
      for (final job in jobs) {
        final middle = ((job.cashMin + job.cashMax) / 2).ceil();
        expect(JobDrama.of(win(middle), job), JobDrama.strong, reason: job.id);
      }
    });

    test('a higher roll never reads as a smaller win', () {
      for (final job in jobs) {
        var previous = JobDrama.clean;
        for (var cash = job.cashMin; cash <= job.cashMax; cash++) {
          final tier = JobDrama.of(win(cash), job);
          expect(tier.index, greaterThanOrEqualTo(previous.index), reason: '${job.id} @ $cash');
          previous = tier;
        }
      }
    });

    test('tiers are relative to the job, not the size of the number', () {
      final byId = {for (final job in jobs) job.id: job};
      final hijack = byId['hijack-a-delivery-truck']!;
      final shakeDown = byId['shake-down-corner-store']!;

      // Hijack's worst win outpays the best win of the corner store many times over, and is still the
      // scrape of its own job.
      expect(hijack.cashMin, greaterThan(shakeDown.cashMax));
      expect(JobDrama.of(win(hijack.cashMin), hijack), JobDrama.clean);
      expect(JobDrama.of(win(shakeDown.cashMax), shakeDown), JobDrama.major);
    });

    test('a big win is rare and a scrape is common, so escalation means something', () {
      for (final job in jobs) {
        final tiers = [
          for (var cash = job.cashMin; cash <= job.cashMax; cash++) JobDrama.of(win(cash), job),
        ];
        double share(JobDrama tier) => tiers.where((t) => t == tier).length / tiers.length;

        expect(share(JobDrama.major), inInclusiveRange(0.05, 0.2), reason: job.id);
        expect(share(JobDrama.clean), inInclusiveRange(0.4, 0.6), reason: job.id);
      }
    });
  });

  group('JobDrama signals', () {
    test('a failure never escalates, whatever it paid', () {
      final job = syntheticJob();
      expect(JobDrama.of(loss(1), job), JobDrama.clean);
      expect(JobDrama.of(loss(job.cashMax), job), JobDrama.clean);
      expect(JobDrama.of(loss(job.cashMax), job, leveledUp: true), JobDrama.clean);
    });

    test('an upset outranks an easy win for the same roll', () {
      final easy = syntheticJob(successChance: 0.95);
      final longShot = syntheticJob(successChance: 0.2);
      final roll = win(130); // 30% of the way up the range

      expect(JobDrama.of(roll, easy), JobDrama.clean);
      expect(JobDrama.of(roll, longShot), JobDrama.strong);
      expect(JobDrama.of(win(170), longShot), JobDrama.major);
      expect(JobDrama.of(win(170), easy), JobDrama.strong);
    });

    test('a rank-up lifts a middling roll', () {
      final job = syntheticJob();
      expect(JobDrama.of(win(130), job), JobDrama.clean);
      expect(JobDrama.of(win(130, rankedUp: true), job), JobDrama.strong);
    });

    test('a level-up lifts a scrape to strong but never lowers a major', () {
      final job = syntheticJob();
      expect(JobDrama.of(win(100), job, leveledUp: true), JobDrama.strong);
      expect(JobDrama.of(win(200), job, leveledUp: true), JobDrama.major);
    });

    test('a job with a fixed payout has no lucky roll', () {
      final flat = syntheticJob(cashMin: 50, cashMax: 50);
      expect(JobDrama.of(win(50), flat), JobDrama.clean);
    });

    test('a payout beyond the range is treated as the top of it', () {
      final job = syntheticJob();
      expect(JobDrama.of(win(500), job), JobDrama.major);
      expect(JobDrama.of(win(1), job), JobDrama.clean);
    });
  });
}
