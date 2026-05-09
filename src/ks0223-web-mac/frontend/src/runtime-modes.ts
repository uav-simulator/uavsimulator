/**
 * Runtime mode the operator UI is talking to.
 *
 *   - `real-robot`: physical KS0223 reachable via the backend Pi-bridge.
 *   - `unity-sim` : Unity simulator instance hosting the runtime HTTP API.
 *
 * Kept in its own module so cross-cutting helpers (storage keys, defaults,
 * connection card) can refer to it without depending on `App.tsx`.
 */
export type RuntimeMode = 'real-robot' | 'unity-sim'

export const RUNTIME_MODES: readonly RuntimeMode[] = ['real-robot', 'unity-sim'] as const

export function isRuntimeMode(value: unknown): value is RuntimeMode {
  return value === 'real-robot' || value === 'unity-sim'
}

export function normalizeRuntimeMode(
  value: unknown,
  fallback: RuntimeMode = 'real-robot',
): RuntimeMode {
  return isRuntimeMode(value) ? value : fallback
}
