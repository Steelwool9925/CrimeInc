import 'package:flutter_test/flutter_test.dart';
import 'package:underworld_empire/api/models.dart';

import 'fakes.dart';

Map<String, dynamic> _objectiveJson({
  String type = 'completeJobTimes',
  int progress = 2,
  bool complete = false,
  bool claimed = false,
  String? jobId = 'shake-down-corner-store',
}) =>
    {
      'id': 'corner-store-regular',
      'name': 'Corner Store Regular',
      'description': 'Shake down corner stores five times today.',
      'type': type,
      'target': 5,
      'jobId': jobId,
      'progress': progress,
      'complete': complete,
      'claimed': claimed,
      'rewardCash': 110,
      'rewardXp': 25,
    };

Map<String, dynamic> _meJson() => {
      'playerId': 'p1',
      'energy': {'current': 5, 'max': 20, 'regenSecondsPerPoint': 90, 'secondsUntilNextPoint': 30},
      'stamina': {'current': 5, 'max': 5, 'regenSecondsPerPoint': 60, 'secondsUntilNextPoint': null},
      'health': {'current': 100, 'max': 100, 'regenSecondsPerPoint': 30, 'secondsUntilNextPoint': null},
      'cash': 10,
      'premiumCurrency': 0,
      'level': 1,
      'xp': 0,
      'skillPoints': 0,
      'attack': 1,
      'defense': 1,
      'dailyStreakCurrent': 0,
      'dailyClaimable': false,
    };

void main() {
  group('DailyObjective.fromJson', () {
    test('reads every field', () {
      final o = DailyObjective.fromJson(_objectiveJson());

      expect(o.id, 'corner-store-regular');
      expect(o.type, 'completeJobTimes');
      expect(o.target, 5);
      expect(o.jobId, 'shake-down-corner-store');
      expect(o.progress, 2);
      expect(o.rewardCash, 110);
      expect(o.rewardXp, 25);
    });

    test('a null jobId means any job counts', () {
      expect(DailyObjective.fromJson(_objectiveJson(jobId: null)).jobId, isNull);
    });

    test('a missing description reads as empty rather than throwing', () {
      final json = _objectiveJson()..remove('description');

      expect(DailyObjective.fromJson(json).description, '');
    });

    test('accepts whole numbers the server wrote as doubles', () {
      final json = _objectiveJson()..['target'] = 5.0;

      expect(DailyObjective.fromJson(json).target, 5);
    });
  });

  group('DailyObjective derived values', () {
    test('fraction is progress over target, never above one', () {
      expect(kObjectiveOpen.fraction, closeTo(0.4, 1e-9));
      expect(kObjectiveDone.fraction, 1.0);
      expect(
        DailyObjective.fromJson(_objectiveJson(progress: 9)).fraction,
        1.0,
        reason: 'a bar never reads past full',
      );
    });

    test('only a finished, unpaid objective is claimable', () {
      expect(kObjectiveOpen.claimable, isFalse);
      expect(kObjectiveDone.claimable, isTrue);
      expect(kObjectiveClaimed.claimable, isFalse);
    });

    test('a collection objective is worked on in Properties, anything else in Jobs', () {
      expect(kObjectiveProperty.destination, ObjectiveDestination.properties);
      expect(kObjectiveOpen.destination, ObjectiveDestination.jobs);
      expect(kObjectiveDone.destination, ObjectiveDestination.jobs);
      expect(
        DailyObjective.fromJson(_objectiveJson(type: 'earnCash')).destination,
        ObjectiveDestination.jobs,
      );
    });
  });

  group('ObjectiveList.fromJson', () {
    test('reads the date, the reset instant as UTC, and the objectives', () {
      final list = ObjectiveList.fromJson({
        'date': '2026-10-13',
        'resetsAtUtc': '2026-10-14T00:00:00+00:00',
        'objectives': [_objectiveJson()],
      });

      expect(list.date, '2026-10-13');
      expect(list.resetsAtUtc, DateTime.utc(2026, 10, 14));
      expect(list.resetsAtUtc.isUtc, isTrue);
      expect(list.objectives, hasLength(1));
    });
  });

  group('ClaimObjectiveResult.fromJson', () {
    test('reads the claimed objective, the player and the full list', () {
      final result = ClaimObjectiveResult.fromJson({
        'claimed': _objectiveJson(progress: 5, complete: true, claimed: true),
        'player': _meJson(),
        'levelUp': null,
        'objectives': [_objectiveJson(progress: 5, complete: true, claimed: true)],
      });

      expect(result.claimed.claimed, isTrue);
      expect(result.player.cash, 10);
      expect(result.levelUp, isNull);
      expect(result.objectives, hasLength(1));
    });

    test('carries a level-up when the reward crossed a level', () {
      final result = ClaimObjectiveResult.fromJson({
        'claimed': _objectiveJson(progress: 5, complete: true, claimed: true),
        'player': _meJson(),
        'levelUp': {
          'levelsGained': 1,
          'newLevel': 2,
          'skillPointsGranted': 1,
          'metersRefilled': true,
          'payoutBonusGained': 0.02,
          'payoutBonus': 0.02,
        },
        'objectives': [],
      });

      expect(result.levelUp?.newLevel, 2);
    });
  });

  group('action responses carrying objectives', () {
    test('do-job reads them when present and tolerates a server that predates them', () {
      final base = {
        'result': {
          'jobId': 'shake-down-corner-store',
          'succeeded': true,
          'energySpent': 1,
          'cashEarned': 15,
          'xpEarned': 10,
          'healthLost': 0,
          'mastery': {
            'completions': 1,
            'rank': 0,
            'maxRank': 5,
            'completionsUntilNextRank': 5,
            'payoutBonus': 0,
          },
          'masteryRankedUp': false,
          'itemsConsumed': [],
        },
        'player': _meJson(),
        'levelUp': null,
      };

      expect(DoJobResult.fromJson(base).objectives, isNull);
      expect(
        DoJobResult.fromJson({
          ...base,
          'objectives': [_objectiveJson()],
        }).objectives,
        hasLength(1),
      );
    });

    test('collect-property reads them when present and tolerates their absence', () {
      final base = {
        'propertyId': 'street-corner',
        'cashCollected': 30,
        'pending': 0,
        'cap': 60,
        'player': _meJson(),
      };

      expect(CollectPropertyResult.fromJson(base).objectives, isNull);
      expect(
        CollectPropertyResult.fromJson({
          ...base,
          'objectives': [_objectiveJson()],
        }).objectives,
        hasLength(1),
      );
    });
  });
}
