import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';

/// One job row as GET /jobs sends it since M3R.
Map<String, dynamic> jobJson({
  int attackRequirement = 3,
  int defenseRequirement = 2,
  double successChance = 0.9,
  bool canAttempt = true,
  String? unavailableReason,
}) =>
    {
      'id': 'shake-down-corner-store',
      'name': 'Shake Down a Corner Store',
      'description': 'Lean on the owner for a cut of the register.',
      'area': 'downtown',
      'energyCost': 1,
      'levelRequirement': 1,
      'cashMin': 15,
      'cashMax': 30,
      'xp': 10,
      'attackRequirement': attackRequirement,
      'defenseRequirement': defenseRequirement,
      'successChance': successChance,
      'mastery': {
        'completions': 3,
        'rank': 0,
        'maxRank': 5,
        'completionsUntilNextRank': 5,
      },
      'canAttempt': canAttempt,
      'unavailableReason': unavailableReason,
    };

/// One result body as POST /jobs/{id}/do sends it since M3R.
Map<String, dynamic> jobResultJson({
  bool succeeded = true,
  int cashEarned = 22,
  int xpEarned = 10,
  int healthLost = 0,
}) =>
    {
      'jobId': 'shake-down-corner-store',
      'succeeded': succeeded,
      'energySpent': 1,
      'cashEarned': cashEarned,
      'xpEarned': xpEarned,
      'healthLost': healthLost,
      'mastery': {
        'completions': 4,
        'rank': 0,
        'maxRank': 5,
        'completionsUntilNextRank': 4,
      },
      'masteryRankedUp': false,
    };

void main() {
  group('Job.fromJson — risk fields', () {
    test('reads the stat requirements', () {
      final job = Job.fromJson(jobJson());

      expect(job.attackRequirement, 3);
      expect(job.defenseRequirement, 2);
    });

    test('reads the live success chance', () {
      final job = Job.fromJson(jobJson(successChance: 0.77));

      expect(job.successChance, closeTo(0.77, 1e-9));
    });

    test('reads a whole-number chance that arrives as an int', () {
      // System.Text.Json writes 1.0 as `1`, and `as double` on an int is a runtime failure in Dart.
      final job = Job.fromJson(jobJson()..['successChance'] = 1);

      expect(job.successChance, closeTo(1.0, 1e-9));
    });

    test('keeps the stats refusal token verbatim', () {
      final job = Job.fromJson(
          jobJson(canAttempt: false, unavailableReason: 'stats'));

      expect(job.canAttempt, isFalse);
      expect(job.unavailableReason, 'stats');
    });
  });

  group('JobResult.fromJson — the failure branch', () {
    test('reads a successful attempt', () {
      final result = JobResult.fromJson(jobResultJson());

      expect(result.succeeded, isTrue);
      expect(result.healthLost, 0);
    });

    test('reads a failed attempt with its consolation and health loss', () {
      final result = JobResult.fromJson(jobResultJson(
        succeeded: false,
        cashEarned: 1,
        xpEarned: 1,
        healthLost: 3,
      ));

      expect(result.succeeded, isFalse);
      expect(result.cashEarned, 1);
      expect(result.xpEarned, 1);
      expect(result.healthLost, 3);
    });
  });
}
