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
  group('LevelUp.fromJson — the pay grant', () {
    Map<String, dynamic> levelUpJson({Object? gained, Object? total, int levels = 1, int newLevel = 4}) => {
          'levelsGained': levels,
          'newLevel': newLevel,
          'skillPointsGranted': levels,
          'metersRefilled': true,
          if (gained != null) 'payoutBonusGained': gained,
          if (total != null) 'payoutBonus': total,
        };

    test('reads the bonus gained and the new total', () {
      final levelUp = LevelUp.fromJson(levelUpJson(gained: 0.02, total: 0.06));

      expect(levelUp.payoutBonusGained, closeTo(0.02, 1e-9));
      expect(levelUp.payoutBonus, closeTo(0.06, 1e-9));
    });

    test('reads whole-number bonuses that arrive as ints', () {
      final levelUp = LevelUp.fromJson(levelUpJson(gained: 0, total: 0));

      expect(levelUp.payoutBonusGained, 0.0);
      expect(levelUp.payoutBonus, 0.0);
    });

    test('an older server that sends no grant reads as granting no pay', () {
      final levelUp = LevelUp.fromJson(levelUpJson());

      expect(levelUp.payoutGainedPercent, 0);
      expect(levelUp.payoutPercent, 0);
    });

    test('rounds both figures to whole percents', () {
      // 3 * 0.02 is 0.06 and 5 * 0.02 is 0.1 in binary floating point only approximately.
      final levelUp = LevelUp.fromJson(levelUpJson(gained: 3 * 0.02, total: 7 * 0.02));

      expect(levelUp.payoutGainedPercent, 6);
      expect(levelUp.payoutPercent, 14);
    });
  });

  group('JobList.fromJson — the level bonus', () {
    Map<String, dynamic> listJson({Object? bonus}) => {
          'jobs': <Map<String, dynamic>>[],
          'currentEnergy': 7,
          'level': 5,
          if (bonus != null) 'levelPayoutBonus': bonus,
        };

    test('reads the bonus the level earns on every job', () {
      expect(JobList.fromJson(listJson(bonus: 0.08)).levelPayoutBonus, closeTo(0.08, 1e-9));
    });

    test('reads a whole-number zero that arrives as an int', () {
      expect(JobList.fromJson(listJson(bonus: 0)).levelPayoutBonus, 0.0);
    });

    test('an older server that sends none reads as no bonus', () {
      expect(JobList.fromJson(listJson()).levelPayoutBonus, 0.0);
    });
  });

  group('JobMastery.fromJson — payout bonus', () {
    Map<String, dynamic> masteryJson({Object? payoutBonus}) => {
          'completions': 12,
          'rank': 2,
          'maxRank': 5,
          'completionsUntilNextRank': 6,
          if (payoutBonus != null) 'payoutBonus': payoutBonus,
        };

    test('reads the bonus the next attempt pays', () {
      final mastery = JobMastery.fromJson(masteryJson(payoutBonus: 0.2));

      expect(mastery.payoutBonus, closeTo(0.2, 1e-9));
    });

    test('reads a whole-number bonus that arrives as an int', () {
      // System.Text.Json writes 0.0 as `0`, and `as double` on an int is a runtime failure in Dart.
      final mastery = JobMastery.fromJson(masteryJson(payoutBonus: 0));

      expect(mastery.payoutBonus, 0.0);
    });

    test('an older server that sends no bonus reads as paying the base', () {
      final mastery = JobMastery.fromJson(masteryJson());

      expect(mastery.payoutBonus, 0.0);
      expect(mastery.payoutLabel, isNull);
    });

    test('labels the bonus as a whole percent', () {
      expect(JobMastery.fromJson(masteryJson(payoutBonus: 0.5)).payoutLabel, '+50% pay');
      // 3 * 0.1 is 0.30000000000000004 in binary floating point; it must still read 30.
      expect(JobMastery.fromJson(masteryJson(payoutBonus: 3 * 0.1)).payoutLabel, '+30% pay');
    });

    test('says nothing at rank 0, where there is no bonus', () {
      expect(JobMastery.fromJson(masteryJson(payoutBonus: 0.0)).payoutLabel, isNull);
    });
  });

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

    test('reads the ceiling and previews the odds of a number of extras', () {
      final job = Job.fromJson(jobJson(successChance: 0.65)
        ..['extraSuccessChance'] = 0.1
        ..['maxSuccessChance'] = 0.8);

      expect(job.maxSuccessChance, 0.8);
      expect(job.chanceWithExtras(0), 0.65);
      expect(job.chanceWithExtras(1), closeTo(0.75, 1e-9));
      expect(job.chanceWithExtras(3), 0.8, reason: 'capped at the ceiling, not 0.95');
    });

    test('a server that sends no ceiling previews as uncapped, and never below the listed odds', () {
      final job = Job.fromJson(jobJson(successChance: 0.9)..['extraSuccessChance'] = 0.2);

      expect(job.maxSuccessChance, 1);
      expect(job.chanceWithExtras(5), 1);
      expect(Job.fromJson(jobJson(successChance: 0.9)).chanceWithExtras(3), 0.9);
    });

    test('counts an item in the singular and the plural', () {
      expect(countedItem(1, 'switchblade'), '1 switchblade');
      expect(countedItem(4, 'switchblade'), '4 switchblades');
      expect(countedItem(2, 'brass-knuckles'), '2 brass-knuckles');
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
