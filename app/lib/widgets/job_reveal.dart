import 'package:flutter/material.dart';

/// Plays one reveal for a [subject] and hands its progress to [builder] as a number from 0 to 1.
///
/// Everything that moves in a reveal (the verdict landing, the stats counting up, any flash) is
/// derived from that single number, driven by a single [AnimationController]. That is what makes the
/// sequence one thing rather than several independent tweens each with its own ticker and its own
/// idea of when it is done. Only the [builder]'s subtree rebuilds as it ticks; nothing above this
/// widget is told.
///
/// The reveal restarts whenever [subject] is a different object, and rests at 0 while it is null.
/// With animations disabled in the OS it never runs: the builder is given 1 straight away, which is
/// the final frame, so a reduced-motion player gets the same screen without the movement. That is
/// also what keeps widget tests deterministic. Nothing here loops, so `pumpAndSettle` always settles.
class JobReveal<T extends Object> extends StatefulWidget {
  const JobReveal({
    super.key,
    required this.subject,
    required this.duration,
    required this.builder,
    this.onRevealed,
  });

  /// What is being revealed. Compared by identity.
  final T? subject;

  /// How long the whole reveal takes.
  final Duration duration;

  final Widget Function(BuildContext context, double progress) builder;

  /// Called once per subject when its reveal has finished, or straight after the first frame when
  /// animations are disabled.
  final ValueChanged<T>? onRevealed;

  @override
  State<JobReveal<T>> createState() => _JobRevealState<T>();
}

class _JobRevealState<T extends Object> extends State<JobReveal<T>>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(vsync: this);

  /// The subject the controller is currently running for.
  T? _running;

  /// The subject whose reveal has played to its end, so it keeps showing its final frame.
  T? _finished;

  /// The subject [JobReveal.onRevealed] has already been told about.
  T? _announced;

  bool _reduce = false;
  bool _ready = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final reduce = MediaQuery.disableAnimationsOf(context);
    if (!_ready) {
      _ready = true;
      _reduce = reduce;
      _sync();
    } else if (reduce != _reduce) {
      _reduce = reduce;
      final subject = _running;
      if (reduce && subject != null) {
        _controller.stop();
        _finish(subject, afterFrame: true);
      }
    }
  }

  @override
  void didUpdateWidget(JobReveal<T> oldWidget) {
    super.didUpdateWidget(oldWidget);
    _sync();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _sync() {
    final subject = widget.subject;
    if (identical(subject, _running)) return;

    _running = subject;
    _controller.stop();
    if (subject == null) {
      _finished = null;
      _announced = null;
      return;
    }

    if (_reduce) {
      _finish(subject, afterFrame: true);
      return;
    }
    _controller.duration = widget.duration;
    // `then`, not `orCancel`: a reveal that is interrupted by a newer one must stay silent.
    _controller.forward(from: 0).then((_) {
      if (mounted && identical(_running, subject)) _finish(subject);
    });
  }

  void _finish(T subject, {bool afterFrame = false}) {
    _finished = subject;
    void announce() {
      if (!mounted || !identical(_running, subject) || identical(_announced, subject)) return;
      _announced = subject;
      widget.onRevealed?.call(subject);
    }

    if (afterFrame) {
      WidgetsBinding.instance.addPostFrameCallback((_) => announce());
    } else {
      announce();
    }
  }

  double get _progress {
    final subject = widget.subject;
    if (subject == null) return 0;
    return _reduce || identical(_finished, subject) ? 1 : _controller.value;
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
        animation: _controller,
        builder: (context, _) => widget.builder(context, _progress),
      );
}
