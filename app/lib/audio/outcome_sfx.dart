import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/foundation.dart';

import '../widgets/job_drama.dart';

/// The four sound cues a job's resolution can play.
enum SfxCue {
  /// The held breath before the verdict.
  riser('riser'),

  /// A win that was a scrape.
  hitLight('hit-light'),

  /// A strong or major win.
  hitHeavy('hit-heavy'),

  /// A failure.
  thud('thud');

  const SfxCue(this.fileName);

  /// The bundled file, without its extension. See [SfxCue.assetPath].
  final String fileName;

  /// Every cue is a short mono mp3: the one format Android, iOS and every browser decode alike,
  /// so there is a single file per cue rather than a per-platform pair.
  String get assetPath => 'sfx/$fileName.mp3';

  /// The cue that lands a job's verdict.
  static SfxCue forOutcome({required bool succeeded, required JobDrama drama}) {
    if (!succeeded) return thud;
    return drama == JobDrama.clean ? hitLight : hitHeavy;
  }
}

/// Plays the cues. An interface so tests inject silence or a recorder rather than a plugin, the
/// same shape as `ApiClient`.
///
/// Audio is decoration: no implementation may throw, and none may do anything before the first
/// [play], which only ever follows a player's tap (browsers refuse sound before a gesture).
abstract class OutcomeSfx {
  /// Starts [cue], cutting off any cue still sounding.
  void play(SfxCue cue);

  /// Cuts off whatever is sounding.
  void stop();

  void dispose();
}

class SilentOutcomeSfx implements OutcomeSfx {
  const SilentOutcomeSfx();

  @override
  void play(SfxCue cue) {}

  @override
  void stop() {}

  @override
  void dispose() {}
}

/// The thin edge over the audioplayers plugin, so [AudioPlayersOutcomeSfx]'s guarantees (one cue at
/// a time, never throws) are testable without a platform channel.
abstract class SfxBackend {
  Future<void> play(String assetPath);
  Future<void> stop();
  Future<void> dispose();
}

class _AudioPlayersBackend implements SfxBackend {
  AudioPlayer? _player;

  /// Created on first use, never at startup: nothing about the plugin is touched until a cue is
  /// actually wanted.
  Future<AudioPlayer> _ensure() async {
    final existing = _player;
    if (existing != null) return existing;

    // Mix with whatever the player is already listening to, and honour the phone's silent switch.
    // Best effort: a platform that cannot set it (the browser) must still get a working player.
    try {
      await AudioPlayer.global.setAudioContext(
        AudioContextConfig(focus: AudioContextConfigFocus.mixWithOthers, respectSilence: true)
            .build(),
      );
    } catch (_) {}
    final created = _player = AudioPlayer();
    await created.setReleaseMode(ReleaseMode.stop);
    await created.setPlayerMode(PlayerMode.lowLatency);
    return created;
  }

  @override
  Future<void> play(String assetPath) async {
    final player = await _ensure();
    await player.play(AssetSource(assetPath));
  }

  @override
  Future<void> stop() async => _player?.stop();

  @override
  Future<void> dispose() async => _player?.dispose();
}

/// Real playback through audioplayers.
///
/// One cue sounds at a time: a new cue is queued behind the previous call and replaces it, so a
/// riser is cut by the verdict and two quick taps never stack. Every failure (a missing asset, a
/// denied autoplay, a plugin fault) is swallowed here, because audio must never be able to fail a
/// job.
class AudioPlayersOutcomeSfx implements OutcomeSfx {
  AudioPlayersOutcomeSfx({@visibleForTesting SfxBackend? backend})
      : _backend = backend ?? _AudioPlayersBackend();

  final SfxBackend _backend;
  Future<void> _tail = Future<void>.value();
  bool _disposed = false;

  @override
  void play(SfxCue cue) {
    if (_disposed) return;
    _enqueue(() async {
      await _backend.stop();
      await _backend.play(cue.assetPath);
    });
  }

  @override
  void stop() {
    if (_disposed) return;
    _enqueue(_backend.stop);
  }

  @override
  void dispose() {
    if (_disposed) return;
    _disposed = true;
    _enqueue(_backend.dispose);
  }

  void _enqueue(Future<void> Function() step) {
    _tail = _tail.then((_) async {
      try {
        await step();
      } catch (_) {}
    });
  }
}
