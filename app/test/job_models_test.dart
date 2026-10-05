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

    test('reads an empty item-requirements array as no gate', () {
      final job = Job.fromJson(jobJson()..['itemRequirements'] = <dynamic>[]);

      expect(job.itemRequirements, isEmpty);
    });

    test('defaults to no item gate when the field is absent entirely', () {
      // Older cached JSON, or any fixture predating this field, must not crash the parser.
      final job = Job.fromJson(jobJson());

      expect(job.itemRequirements, isEmpty);
    });

    test('reads an item requirement with the callers held quantity', () {
      final job = Job.fromJson(jobJson(canAttempt: false, unavailableReason: 'items')
        ..['itemRequirements'] = [
          {'itemId': 'switchblade', 'minQuantity': 2, 'held': 1, 'met': false},
        ]);

      final requirement = job.itemRequirements.single;
      expect(requirement.itemId, 'switchblade');
      expect(requirement.minQuantity, 2);
      expect(requirement.held, 1);
      expect(requirement.met, isFalse);
      expect(job.unavailableReason, 'items');
    });

    test('reads how many extras the player may add', () {
      final job = Job.fromJson(jobJson()
        ..['itemRequirements'] = [
          {'itemId': 'switchblade', 'minQuantity': 1, 'maxExtras': 3, 'held': 4, 'met': true},
        ]
        ..['extraSuccessChance'] = 0.1);

      expect(job.itemRequirements.single.maxExtras, 3);
      expect(job.extraSuccessChance, 0.1);
    });

    test('a requirement with no maxExtras allows none', () {
      // A server that predates the field must still read correctly.
      final job = Job.fromJson(jobJson()
        ..['itemRequirements'] = [
          {'itemId': 'switchblade', 'minQuantity': 2, 'held': 2, 'met': true},
        ]);

      expect(job.itemRequirements.single.maxExtras, 0);
      expect(job.extraSuccessChance, 0);
    });

    test('how many extras could be added is capped by the content and by the spare stock', () {
      JobItemRequirement r(int min, int maxExtras, int held) => JobItemRequirement(
          itemId: 'switchblade', minQuantity: min, maxExtras: maxExtras, held: held, met: held >= min);

      expect(r(1, 3, 4).availableExtras, 3, reason: 'plenty of stock: the content allowance');
      expect(r(1, 3, 2).availableExtras, 1, reason: 'stock is the limit: one spare after the requirement');
      expect(r(1, 3, 1).availableExtras, 0, reason: 'exactly the requirement: nothing to add');
      expect(r(2, 3, 1).availableExtras, 0, reason: 'short of the requirement: never negative');
      expect(r(1, 0, 10).availableExtras, 0, reason: 'the content allows none');
    });

    test('reads a met item requirement', () {
      final job = Job.fromJson(jobJson()
        ..['itemRequirements'] = [
          {'itemId': 'switchblade', 'minQuantity': 2, 'held': 2, 'met': true},
        ]);

      expect(job.itemRequirements.single.met, isTrue);
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

    test('defaults to no consumption when the field is absent entirely', () {
      final result = JobResult.fromJson(jobResultJson());

      expect(result.itemsConsumed, isEmpty);
    });

    test('reads an items-consumed entry', () {
      final result = JobResult.fromJson(jobResultJson()
        ..['itemsConsumed'] = [
          {'itemId': 'switchblade', 'quantityConsumed': 1, 'quantityRemaining': 4},
        ]);

      final consumed = result.itemsConsumed.single;
      expect(consumed.itemId, 'switchblade');
      expect(consumed.quantityConsumed, 1);
      expect(consumed.quantityRemaining, 4);
    });
  });
}
