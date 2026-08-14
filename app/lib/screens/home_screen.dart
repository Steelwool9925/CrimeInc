import 'package:flutter/material.dart';

import '../api/models.dart';
import '../state/app_state.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, required this.appState});

  final AppState appState;

  Color? _parsePrimaryColor(String? hex) {
    if (hex == null) return null;
    final clean = hex.replaceFirst('#', '');
    final value = int.tryParse(
      clean.length == 6 ? 'FF$clean' : clean,
      radix: 16,
    );
    if (value == null) return null;
    return Color(value);
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: appState,
      builder: (context, _) {
        final pack = appState.contentPack;
        final primaryColor = _parsePrimaryColor(pack?.primaryColorHex);
        final theme = Theme.of(context);
        final appBarColor = primaryColor ?? theme.colorScheme.primary;

        return Scaffold(
          appBar: AppBar(
            backgroundColor: appBarColor,
            foregroundColor: Colors.white,
            title: const Text('Underworld Empire'),
          ),
          body: _buildBody(context, appState),
        );
      },
    );
  }

  Widget _buildBody(BuildContext context, AppState state) {
    switch (state.status) {
      case LoadStatus.loading:
        return const Center(child: CircularProgressIndicator());
      case LoadStatus.error:
        return Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                state.errorMessage ?? 'An error occurred',
                textAlign: TextAlign.center,
                style: const TextStyle(color: Colors.red),
              ),
              const SizedBox(height: 16),
              ElevatedButton(
                onPressed: appState.bootstrap,
                child: const Text('Retry'),
              ),
            ],
          ),
        );
      case LoadStatus.ready:
        return _buildReadyBody(context, state);
    }
  }

  Widget _buildReadyBody(BuildContext context, AppState state) {
    final me = state.me!;
    final pack = state.contentPack;
    final cashLabel = pack?.cashLabel ?? 'Cash';

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _MeterCard(label: 'Energy', meter: me.energy),
          const SizedBox(height: 8),
          _MeterCard(label: 'Stamina', meter: me.stamina),
          const SizedBox(height: 8),
          _MeterCard(label: 'Health', meter: me.health),
          const SizedBox(height: 16),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    cashLabel,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    '${me.cash}',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _MeterCard extends StatelessWidget {
  const _MeterCard({required this.label, required this.meter});

  final String label;
  final MeterState meter;

  @override
  Widget build(BuildContext context) {
    final progress = meter.max > 0 ? meter.current / meter.max : 0.0;
    final isFull = meter.current >= meter.max;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  label,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                Text(
                  '${meter.current} / ${meter.max}',
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
              ],
            ),
            const SizedBox(height: 8),
            LinearProgressIndicator(value: progress),
            if (!isFull && meter.secondsUntilNextPoint != null) ...[
              const SizedBox(height: 4),
              Text(
                '+1 in ${meter.secondsUntilNextPoint}s',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
