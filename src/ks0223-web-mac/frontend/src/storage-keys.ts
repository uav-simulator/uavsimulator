/**
 * Centralised registry of `localStorage` keys used by the operator Web UI.
 *
 * Why this file exists: previously these 18+ key strings lived as `const`
 * declarations scattered across `App.tsx`. Renaming a key required hunting
 * call sites by hand, and there was no single place to audit what the app
 * persists in the user's browser. Treat this module as the source of truth.
 *
 * Naming: every key is prefixed `ks0223_` so multiple deploys on the same
 * origin (e.g. `localhost:5173` for two checkouts) do not collide. When you
 * change the SHAPE of a stored value, bump the suffix (`_v2`, `_v3`) so old
 * data does not crash the parser.
 *
 * For per-runtime-mode keys (host, port) use the helpers exported below
 * rather than concatenating the prefix manually.
 */

import type { RuntimeMode } from './runtime-modes'

// --- session/runtime ---------------------------------------------------------
export const RUNTIME_MODE_STORAGE_KEY = 'ks0223_runtime_mode'
export const CLIENT_INSTANCE_ID_STORAGE_KEY = 'ks0223_client_instance_id'

// --- target host/port: keyed per RuntimeMode (real-robot vs unity-sim) ------
export const TARGET_HOST_STORAGE_KEY_PREFIX = 'ks0223_target_host_'
export const TARGET_PORT_STORAGE_KEY_PREFIX = 'ks0223_target_port_'

export function targetHostStorageKey(mode: RuntimeMode): string {
  return `${TARGET_HOST_STORAGE_KEY_PREFIX}${mode}`
}

export function targetPortStorageKey(mode: RuntimeMode): string {
  return `${TARGET_PORT_STORAGE_KEY_PREFIX}${mode}`
}

// --- Unity scenario selection -----------------------------------------------
export const UNITY_TRACK_STORAGE_KEY = 'ks0223_unity_track_id'
export const UNITY_VEHICLE_STORAGE_KEY = 'ks0223_unity_vehicle_id'
export const UNITY_CAMERA_MODE_STORAGE_KEY = 'ks0223_unity_camera_mode'
export const UNITY_CONTROL_AGENT_STORAGE_KEY = 'ks0223_unity_control_agent'
export const UNITY_CAMERA_AGENT_STORAGE_KEY = 'ks0223_unity_camera_agent'
export const UNITY_COLLISIONS_ENABLED_STORAGE_KEY = 'ks0223_unity_collisions_enabled'
export const UNITY_SEE_EACH_OTHER_STORAGE_KEY = 'ks0223_unity_see_each_other'

// Legacy: kept only so we can `removeItem` it during migration. Do NOT read.
export const LEGACY_UNITY_SECONDARY_VEHICLE_STORAGE_KEY = 'ks0223_unity_secondary_vehicle_id'

// --- control surfaces (drive/camera/sensors) --------------------------------
export const DRIVE_SPEED_STORAGE_KEY = 'ks0223_drive_speed_percent'
export const CAMERA_SPEED_STORAGE_KEY = 'ks0223_camera_speed_percent'
export const CAMERA_PAN_STORAGE_KEY = 'ks0223_camera_pan_deg'
export const CAMERA_TILT_STORAGE_KEY = 'ks0223_camera_tilt_deg'
export const ULTRASONIC_ANGLE_STORAGE_KEY = 'ks0223_ultrasonic_angle_deg'
export const ULTRASONIC_AUTO_SCAN_STORAGE_KEY = 'ks0223_ultrasonic_auto_scan'
export const ULTRASONIC_SERVO_PIN_STORAGE_KEY = 'ks0223_ultrasonic_servo_pin'

// One-shot migration flag: forces ULTRASONIC_AUTO_SCAN_STORAGE_KEY off once
// per browser when the auto-scan default flipped from on→off.
export const ULTRASONIC_AUTO_SCAN_MIGRATION_V2_KEY = 'ks0223_ultrasonic_auto_scan_migration_v2'
