import '../api/models.dart';

/// How big a win a job result was, so the reveal can be as loud as the win deserves.
///
/// Everything here is read off a result and its job: nothing new comes from the server. It is
/// deliberately a pure function with no widget in sight, so any future outcome surface (PvP, a
/// property collect) can ask the same question and "a big win looks bigger" is something a test can
/// assert rather than something to hope for.
enum JobDrama {
  /// A scrape, or any failure: the verdict alone.
  clean,

  /// A good roll, an upset, or a rank-up: the verdict plus staggered stats and speedlines.
  strong,

  /// A top-of-the-range roll, or a big upset: everything above plus a halftone flash.
  major;

  /// A roll this far up the job's cash range, or further, is a strong win.
  static const double _strongAt = 0.5;

  /// ... and this far up is a major one: the top tenth of the range.
  static const double _majorAt = 0.9;

  /// Success chances below this count as an upset, and the shortfall is added to the score — beating
  /// a 20% job should feel bigger than clearing a 95% one for the same payout.
  static const double _upsetBelow = 0.5;

  /// What ranking up a job's mastery adds to the score.
  static const double _rankUpBonus = 0.25;

  /// How dramatic [result], the outcome of attempting [job], was.
  ///
  /// The score is where the cash roll landed *within the job's own range*, not the amount: a 400
  /// roll on a job that pays 400–620 is a scrape however large 400 looks next to a job that pays
  /// 15–30. A failure never escalates, since its own treatment is the weight of the failure.
  /// [leveledUp] lifts a win to at least [strong], because levelling up is the biggest beat the game
  /// has and a scrape should not be allowed to undersell it.
  static JobDrama of(JobResult result, Job job, {bool leveledUp = false}) {
    if (!result.succeeded) return clean;

    // A job that always pays the same has no lucky roll to celebrate.
    final span = job.cashMax - job.cashMin;
    final roll = span <= 0 ? 0.0 : ((result.cashEarned - job.cashMin) / span).clamp(0.0, 1.0);
    final upset = (_upsetBelow - job.successChance).clamp(0.0, _upsetBelow);
    final score = roll + upset + (result.masteryRankedUp ? _rankUpBonus : 0);

    final tier = score >= _majorAt
        ? major
        : score >= _strongAt
            ? strong
            : clean;
    return leveledUp && tier == clean ? strong : tier;
  }
}
