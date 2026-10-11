import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:underworld_empire/api/models.dart';
import 'package:underworld_empire/audio/outcome_sfx.dart';
import 'package:underworld_empire/state/app_state.dart';
import 'package:underworld_empire/storage/sound_preference_store.dart';
import 'package:underworld_empire/widgets/job_drama.dart';

import 'fakes.dart';

/// Logs every call and can be told to be slow or to explode, so the player's guarantees can be
/// asserted without a platform channel.
class _FakeBackend implements SfxBackend {
  final List<String> calls = [];
  int running = 0;
  int maxRunning = 0;
  Duration playTime = Duration.zero;
  Object? failWith;
  final List<(int, Completer<void>)> _waiters = [];

  /// Completes once the backend has been called [count] times — so a test waits on the event
  /// itself rather than guessing how many milliseconds it takes.
  Future<void> waitForCalls(int count) {
    if (calls.length >= count) return Future<void>.value();
    final done = Completer<void>();
    _waiters.add((count, done));
    return done.future;
  }

  Future<void> _track(String call, Duration time) async {
    calls.add(call);
    for (final (count, done) in _waiters) {
      if (calls.length >= count && !done.isCompleted) done.complete();
    }
    running++;
    if (running > maxRunning) maxRunning = running;
    try {
      if (time > Duration.zero) await Future<void>.delayed(time);
      if (failWith != null) throw failWith!;
    } finally {
      running--;
    }
  }

  @override
  Future<void> play(String assetPath) => _track('play $assetPath', playTime);

  @override
  Future<void> stop() => _track('stop', Duration.zero);

  @override
  Future<void> dispose() => _track('dispose', Duration.zero);
}

class _ThrowingSoundStore implements SoundPreferenceStore {
  @override
  Future<bool?> read() async => throw StateError('no disk');

  @override
  Future<void> write(bool enabled) async => throw StateError('no disk');
}

void main() {
  group('SfxCue', () {
    test('a failure is a thud however dramatic the job otherwise was', () {
      for (final drama in JobDrama.values) {
        expect(SfxCue.forOutcome(succeeded: false, drama: drama), SfxCue.thud);
      }
    });

    test('a scrape of a win is light; a strong or major win is heavy', () {
      expect(SfxCue.forOutcome(succeeded: true, drama: JobDrama.clean), SfxCue.hitLight);
      expect(SfxCue.forOutcome(succeeded: true, drama: JobDrama.strong), SfxCue.hitHeavy);
      expect(SfxCue.forOutcome(succeeded: true, drama: JobDrama.major), SfxCue.hitHeavy);
    });

    test('each cue has its own bundled file', () {
      final paths = SfxCue.values.map((c) => c.assetPath).toSet();
      expect(paths, hasLength(SfxCue.values.length));
      expect(paths, containsAll(['sfx/riser.mp3', 'sfx/hit-light.mp3', 'sfx/hit-heavy.mp3']));
      expect(paths, contains('sfx/thud.mp3'));
    });
  });

  group('AudioPlayersOutcomeSfx', () {
    test('touches nothing until a cue is asked for', () async {
      final backend = _FakeBackend();
      AudioPlayersOutcomeSfx(backend: backend);
      await pumpEventQueue();

      expect(backend.calls, isEmpty);
    });

    test('a cue cuts off whatever was sounding, then plays', () async {
      final backend = _FakeBackend();
      AudioPlayersOutcomeSfx(backend: backend).play(SfxCue.riser);
      await backend.waitForCalls(2);

      expect(backend.calls, ['stop', 'play sfx/riser.mp3']);
    });

    test('two rapid taps never overlap: every call waits for the one before', () async {
      final backend = _FakeBackend()..playTime = const Duration(milliseconds: 15);
      final sfx = AudioPlayersOutcomeSfx(backend: backend);

      sfx.play(SfxCue.riser);
      sfx.play(SfxCue.hitHeavy);
      sfx.play(SfxCue.thud);
      await backend.waitForCalls(6);

      expect(backend.maxRunning, 1);
      expect(backend.calls, [
        'stop', 'play sfx/riser.mp3',
        'stop', 'play sfx/hit-heavy.mp3',
        'stop', 'play sfx/thud.mp3',
      ]);
    });

    test('a playback failure is swallowed and the next cue still plays', () async {
      final backend = _FakeBackend()..failWith = StateError('autoplay denied');
      final sfx = AudioPlayersOutcomeSfx(backend: backend);
      final uncaught = <Object>[];

      await runZonedGuarded(() async {
        sfx.play(SfxCue.hitLight);
        await backend.waitForCalls(1);
        backend.failWith = null;
        sfx.play(SfxCue.thud);
        await backend.waitForCalls(3);
      }, (error, _) => uncaught.add(error));

      expect(uncaught, isEmpty);
      expect(backend.calls.last, 'play sfx/thud.mp3');
    });

    test('nothing sounds after dispose', () async {
      final backend = _FakeBackend();
      final sfx = AudioPlayersOutcomeSfx(backend: backend);

      sfx.dispose();
      sfx.play(SfxCue.thud);
      sfx.stop();
      await backend.waitForCalls(1);
      await pumpEventQueue();

      expect(backend.calls, ['dispose']);
    });
  });

  group('AppState — what sounds, and when', () {
    const failed = DoJobResult(result: kFailedJobResult, player: kFakeMe);

    Future<({AppState state, RecordingOutcomeSfx sfx})> build({
      FakeApiClient? api,
      Duration revealDelay = Duration.zero,
      bool soundEnabled = true,
      SoundPreferenceStore? store,
    }) async {
      final sfx = RecordingOutcomeSfx();
      final state = AppState(
        apiClient: api ?? FakeApiClient(),
        tokenStore: InMemoryTokenStore(),
        revealDelay: revealDelay,
        sfx: sfx,
        soundStore: store,
        soundEnabled: soundEnabled,
      );
      await state.bootstrap();
      await state.loadJobs();
      return (state: state, sfx: sfx);
    }

    test('nothing sounds on startup or while browsing', () async {
      final h = await build(revealDelay: const Duration(milliseconds: 10));

      expect(h.sfx.played, isEmpty);
    });

    test('a held win rises, then lands the cue its drama earns', () async {
      final h = await build(revealDelay: const Duration(milliseconds: 10));

      await h.state.doJob('shake-down-corner-store');

      expect(h.sfx.played.first, SfxCue.riser);
      expect(h.sfx.played, hasLength(2));
      expect(
        h.sfx.played.last,
        SfxCue.forOutcome(succeeded: true, drama: h.state.lastJobDrama!),
      );
    });

    test('a held failure rises, then thuds', () async {
      final h = await build(
        api: FakeApiClient(doJobResult: failed),
        revealDelay: const Duration(milliseconds: 10),
      );

      await h.state.doJob('shake-down-corner-store');

      expect(h.sfx.played, [SfxCue.riser, SfxCue.thud]);
    });

    test('with no hold there is no riser, only the verdict', () async {
      final h = await build(api: FakeApiClient(doJobResult: failed));

      await h.state.doJob('shake-down-corner-store');

      expect(h.sfx.played, [SfxCue.thud]);
    });

    test('a refused job is silent', () async {
      final h = await build(
        api: FakeApiClient(refuseDoJobWith: 'energy'),
        revealDelay: const Duration(milliseconds: 10),
      );

      await h.state.doJob('shake-down-corner-store');

      expect(h.sfx.played, isEmpty);
    });

    test('muted: a whole job, hold included, makes no sound', () async {
      final h = await build(revealDelay: const Duration(milliseconds: 10), soundEnabled: false);

      await h.state.doJob('shake-down-corner-store');

      expect(h.sfx.played, isEmpty);
      expect(h.state.lastJobResult, isNotNull);
    });

    test('muting mid-hold cuts the riser and silences the verdict', () async {
      final h = await build(revealDelay: const Duration(hours: 1));

      final done = h.state.doJob('shake-down-corner-store');
      for (var i = 0; i < 100 && !h.state.anticipating; i++) {
        await Future<void>.delayed(Duration.zero);
      }
      await h.state.setSoundEnabled(false);
      h.state.skipReveal();
      await done;

      expect(h.sfx.played, [SfxCue.riser]);
      expect(h.sfx.stops, 1);
    });

    test('the mute is saved, and unmuting restores the cues', () async {
      final store = InMemorySoundPreferenceStore();
      final h = await build(store: store);

      await h.state.setSoundEnabled(false);
      expect(h.state.soundEnabled, isFalse);
      expect(await store.read(), isFalse);

      await h.state.setSoundEnabled(true);
      await h.state.doJob('shake-down-corner-store');
      expect(await store.read(), isTrue);
      expect(h.sfx.played, isNotEmpty);
    });

    test('a store that cannot save does not break the toggle', () async {
      final h = await build(store: _ThrowingSoundStore());

      await h.state.setSoundEnabled(false);

      expect(h.state.soundEnabled, isFalse);
    });

    test('disposing cuts off any cue still sounding', () async {
      final h = await build();

      h.state.dispose();

      expect(h.sfx.stops, 1);
    });
  });

  group('SharedPrefsSoundPreferenceStore', () {
    setUp(() => SharedPreferences.setMockInitialValues({}));

    test('has no opinion until the player chooses, so sound defaults on', () async {
      expect(await SharedPrefsSoundPreferenceStore().read(), isNull);
    });

    test('a mute survives a restart (a fresh store reads it back)', () async {
      await SharedPrefsSoundPreferenceStore().write(false);

      expect(await SharedPrefsSoundPreferenceStore().read(), isFalse);
    });
  });
}
