import type {
  CameraStatusDto,
  CommandResponse,
  HealthDto,
  LogFileInfo,
  LogState,
  SensorBridgeResponse,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
} from './types'

const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

const withBase = (path: string) => `${apiBase}${path}`

async function handleJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || `${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

export async function fetchStatus(): Promise<StatusDto> {
  const response = await fetch(withBase('/api/status'))
  return handleJson<StatusDto>(response)
}

export async function connectPi(host: string, port?: number): Promise<StatusDto> {
  const response = await fetch(withBase('/api/connection/connect'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ host, port }),
  })
  return handleJson<StatusDto>(response)
}

export async function disconnectPi(): Promise<StatusDto> {
  const response = await fetch(withBase('/api/connection/disconnect'), { method: 'POST' })
  return handleJson<StatusDto>(response)
}

export async function sendCommand(command: string): Promise<CommandResponse> {
  const response = await fetch(withBase('/api/command'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ command }),
  })

  return handleJson<CommandResponse>(response)
}

export async function startLogging(tag: string): Promise<LogState> {
  const response = await fetch(withBase('/api/logs/start'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tag }),
  })

  return handleJson<LogState>(response)
}

export async function stopLogging(): Promise<LogState> {
  const response = await fetch(withBase('/api/logs/stop'), { method: 'POST' })
  return handleJson<LogState>(response)
}

export async function fetchLogFiles(): Promise<LogFileInfo[]> {
  const response = await fetch(withBase('/api/logs/files'))
  return handleJson<LogFileInfo[]>(response)
}

export async function openLogsFolder(): Promise<void> {
  const response = await fetch(withBase('/api/logs/open-folder'), { method: 'POST' })
  await handleJson<{ opened: boolean }>(response)
}

export async function fetchCameraStatus(): Promise<CameraStatusDto> {
  const response = await fetch(withBase('/api/camera/status'))
  return handleJson<CameraStatusDto>(response)
}

export async function fetchHealth(): Promise<HealthDto> {
  const response = await fetch(withBase('/api/health'))
  return handleJson<HealthDto>(response)
}

export async function fetchSensorStatus(): Promise<SensorBridgeStatusDto> {
  const response = await fetch(withBase('/api/sensors/status'))
  return handleJson<SensorBridgeStatusDto>(response)
}

export async function fetchSensorLatest(): Promise<SensorTelemetryDto | null> {
  const response = await fetch(withBase('/api/sensors/latest'))
  if (response.status === 404) {
    return null
  }

  return handleJson<SensorTelemetryDto>(response)
}

export async function updateSensorConfig(payload: {
  autoScanEnabled?: boolean
  sampleIntervalMs?: number
  scanIntervalSec?: number
  scanSettleMs?: number
  driveSpeedPercent?: number
  cameraSpeedPercent?: number
  ultrasonicServoPin?: number
}): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams()
  if (payload.autoScanEnabled !== undefined) params.set('autoScanEnabled', String(payload.autoScanEnabled))
  if (payload.sampleIntervalMs !== undefined) params.set('sampleIntervalMs', String(payload.sampleIntervalMs))
  if (payload.scanIntervalSec !== undefined) params.set('scanIntervalSec', String(payload.scanIntervalSec))
  if (payload.scanSettleMs !== undefined) params.set('scanSettleMs', String(payload.scanSettleMs))
  if (payload.driveSpeedPercent !== undefined) params.set('driveSpeedPercent', String(payload.driveSpeedPercent))
  if (payload.cameraSpeedPercent !== undefined) params.set('cameraSpeedPercent', String(payload.cameraSpeedPercent))
  if (payload.ultrasonicServoPin !== undefined) params.set('ultrasonicServoPin', String(payload.ultrasonicServoPin))

  const response = await fetch(withBase(`/api/sensors/config?${params.toString()}`), { method: 'POST' })

  return handleJson<SensorBridgeResponse>(response)
}

export async function setUltrasonicPosition(
  angleDeg: number,
  disableAutoScan = true,
  servoPin?: number,
): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({
    angleDeg: String(angleDeg),
    disableAutoScan: String(disableAutoScan),
  })
  if (servoPin !== undefined) {
    params.set('servoPin', String(servoPin))
  }
  const response = await fetch(withBase(`/api/sensors/ultrasonic/position?${params.toString()}`), {
    method: 'POST',
  })
  return handleJson<SensorBridgeResponse>(response)
}

export async function setUltrasonicAutoScan(enabled: boolean): Promise<SensorBridgeResponse> {
  const response = await fetch(withBase(`/api/sensors/ultrasonic/auto-scan?enabled=${String(enabled)}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledSetPattern(pattern: string): Promise<SensorBridgeResponse> {
  const response = await fetch(withBase(`/api/led/pattern?pattern=${encodeURIComponent(pattern)}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledSetCustomFrame(frameHex: string): Promise<SensorBridgeResponse> {
  const response = await fetch(withBase(`/api/led/custom?frameHex=${encodeURIComponent(frameHex)}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledClear(): Promise<SensorBridgeResponse> {
  const response = await fetch(withBase('/api/led/clear'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
  return handleJson<SensorBridgeResponse>(response)
}

export function cameraMjpegUrl(): string {
  return withBase('/api/camera/mjpeg')
}

export function resolveHubUrl(): string {
  return withBase('/hub/telemetry')
}
