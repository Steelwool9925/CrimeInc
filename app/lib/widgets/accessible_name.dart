import 'package:flutter/material.dart';

/// Gives a button an accessible name that says more than its visible label.
///
/// Wrap a button's *child* in this. Visually the buttons on a list screen are just "Buy" or
/// "Equip" — the card around each one supplies which item it belongs to, and a sighted player gets
/// that at a glance. A screen-reader user working from a list of buttons gets none of it: six
/// buttons all named "Buy" are indistinguishable, and nine on the Market worse. [name] carries the
/// missing context (and, for a disabled button, why it is disabled, since that reason otherwise
/// sits in a separate node nothing ties to the control it refuses).
///
/// `excludeSemantics` is deliberate: the visible [visible] child (a "Buy" label, or a spinner while
/// the action is in flight) must not add its own words to [name], or the control reads "Buy Street
/// Corner Buy". Putting the label here rather than on the label's `Text` also means the accessible
/// name survives the busy state, when the visible child is a spinner with no text at all.
class AccessibleName extends StatelessWidget {
  const AccessibleName({super.key, required this.name, required this.visible});

  /// The full spoken name, e.g. `Buy Street Corner`.
  final String name;

  /// What is drawn inside the button.
  final Widget visible;

  @override
  Widget build(BuildContext context) =>
      Semantics(label: name, excludeSemantics: true, child: visible);
}
