import type {
  AutopilotStatusDto,
  CameraStatusDto,
  CommandResponse,
  HealthDto,
  LogFileInfo,
  LogState,
  ModelBindingDto,
  ModelCatalogEntryDto,
  ModelInfoDto,
  SensorBridgeResponse,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
  UnityRuntimeCatalogDto,
} from './types'

const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

const withBase = (path: string) => `${apiBase}${path}`

function withClientRuntime(path: string, clientId: string, runtimeMode: string): string {
  const params = new URLSearchParams({ clientId, runtimeMode })
  const query = params.toString()
  return withBase(`${path}${path.includes('?') ? '&' : '?'}${query}`)
}

async function handleJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || `${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

export async function fetchStatus(clientId: string, runtimeMode: string): Promise<StatusDto> {
  const response = await fetch(withClientRuntime('/api/status', clientId, runtimeMode))
  return handleJson<StatusDto>(response)
}

export async function connectPi(clientId: string, runtimeMode: string, host: string, port?: number): Promise<StatusDto> {
  const response = await fetch(withBase('/api/connection/connect'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId, runtimeMode, host, port }),
  })
  return handleJson<StatusDto>(response)
}

export async function disconnectPi(clientId: string, runtimeMode: string): Promise<StatusDto> {
  const response = await fetch(withBase('/api/connection/disconnect'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId, runtimeMode }),
  })
  return handleJson<StatusDto>(response)
}

export async function fetchUnityRuntimeCatalog(clientId: string, runtimeMode: string, host?: string, port?: number): Promise<UnityRuntimeCatalogDto> {
  const params = new URLSearchParams({ clientId, runtimeMode })
  if (host?.trim()) {
    params.set('host', host.trim())
  }
  if (port !== undefined) {
    params.set('port', String(port))
  }

  const response = await fetch(withBase(`/api/unity/runtime-catalog?${params.toString()}`))
  return handleJson<UnityRuntimeCatalogDto>(response)
}

export async function setUnityRuntimeSelection(payload: {
  clientId: string
  runtimeMode: string
  trackId?: string
  vehicleId?: string
  cameraMode?: string
  agents?: Array<{
    agentId?: string
    vehicleId?: string
    isPrimary?: boolean
  }>
  applyImmediately?: boolean
  collisionsEnabled?: boolean
  seeEachOther?: boolean
}): Promise<UnityRuntimeCatalogDto> {
  const response = await fetch(withBase('/api/unity/runtime-selection'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return handleJson<UnityRuntimeCatalogDto>(response)
}

export async function setUnityClientSelection(payload: {
  clientId: string
  runtimeMode: string
  controlAgentId?: string
  cameraAgentId?: string
}): Promise<UnityRuntimeCatalogDto> {
  const response = await fetch(withBase('/api/unity/client-selection'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return handleJson<UnityRuntimeCatalogDto>(response)
}

export async function sendCommand(
  clientId: string,
  runtimeMode: string,
  command: string,
  agentId?: string,
): Promise<CommandResponse> {
  const response = await fetch(withBase('/api/command'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId, runtimeMode, command, agentId }),
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

export async function fetchCameraStatus(clientId: string, runtimeMode: string): Promise<CameraStatusDto> {
  const response = await fetch(withClientRuntime('/api/camera/status', clientId, runtimeMode))
  return handleJson<CameraStatusDto>(response)
}

export async function fetchHealth(clientId: string, runtimeMode: string): Promise<HealthDto> {
  const response = await fetch(withClientRuntime('/api/health', clientId, runtimeMode))
  return handleJson<HealthDto>(response)
}

export async function fetchSensorStatus(clientId: string, runtimeMode: string): Promise<SensorBridgeStatusDto> {
  const response = await fetch(withClientRuntime('/api/sensors/status', clientId, runtimeMode))
  return handleJson<SensorBridgeStatusDto>(response)
}

export async function fetchSensorLatest(clientId: string, runtimeMode: string): Promise<SensorTelemetryDto | null> {
  const response = await fetch(withClientRuntime('/api/sensors/latest', clientId, runtimeMode))
  if (response.status === 404) {
    return null
  }

  return handleJson<SensorTelemetryDto>(response)
}

export async function updateSensorConfig(
  clientId: string,
  runtimeMode: string,
  payload: {
    autoScanEnabled?: boolean
    sampleIntervalMs?: number
    scanIntervalSec?: number
    scanSettleMs?: number
    driveSpeedPercent?: number
    cameraSpeedPercent?: number
    ultrasonicServoPin?: number
  },
): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({ clientId, runtimeMode })
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

export async function uploadModelArtifact(payload: {
  file: File
  name?: string
  version?: string
  source?: string
  metadata?: string
  metrics?: string
}): Promise<ModelInfoDto> {
  const form = new FormData()
  form.set('file', payload.file)
  if (payload.name?.trim()) form.set('name', payload.name.trim())
  if (payload.version?.trim()) form.set('version', payload.version.trim())
  if (payload.source?.trim()) form.set('source', payload.source.trim())
  if (payload.metadata?.trim()) form.set('metadata', payload.metadata.trim())
  if (payload.metrics?.trim()) form.set('metrics', payload.metrics.trim())

  const response = await fetch(withBase('/api/models/upload'), {
    method: 'POST',
    body: form,
  })
  return handleJson<ModelInfoDto>(response)
}

export async function fetchModels(): Promise<ModelInfoDto[]> {
  const response = await fetch(withBase('/api/models'))
  return handleJson<ModelInfoDto[]>(response)
}

export async function fetchModelCatalog(): Promise<ModelCatalogEntryDto[]> {
  const response = await fetch(withBase('/api/model-catalog'))
  return handleJson<ModelCatalogEntryDto[]>(response)
}

export async function activateModel(modelId: string): Promise<ModelInfoDto> {
  const response = await fetch(withBase('/api/models/activate'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ modelId }),
  })
  return handleJson<ModelInfoDto>(response)
}

export async function fetchActiveModel(): Promise<ModelInfoDto | null> {
  const response = await fetch(withBase('/api/models/active'))
  if (response.status === 404) {
    return null
  }

  return handleJson<ModelInfoDto>(response)
}

export async function fetchModelBinding(
  clientId: string,
  runtimeMode: string,
  agentId?: string,
): Promise<ModelBindingDto | null> {
  const params = new URLSearchParams({ clientId, runtimeMode })
  if (agentId?.trim()) {
    params.set('agentId', agentId.trim())
  }

  const response = await fetch(withBase(`/api/model-bindings/current?${params.toString()}`))
  if (response.status === 404) {
    return null
  }

  return handleJson<ModelBindingDto>(response)
}

export async function setModelBinding(payload: {
  clientId: string
  runtimeMode: string
  modelId: string
  agentId?: string
}): Promise<ModelBindingDto> {
  const response = await fetch(withBase('/api/model-bindings'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return handleJson<ModelBindingDto>(response)
}

export async function startAutopilot(payload: {
  clientId: string
  runtimeMode: string
  agentId?: string
  modelId?: string
  loopIntervalMs?: number
}): Promise<AutopilotStatusDto> {
  const response = await fetch(withBase('/api/autopilot/start'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  return handleJson<AutopilotStatusDto>(response)
}

export async function stopAutopilot(payload?: { clientId?: string; runtimeMode?: string }): Promise<AutopilotStatusDto> {
  const response = await fetch(withBase('/api/autopilot/stop'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload ?? {}),
  })
  return handleJson<AutopilotStatusDto>(response)
}

export type ImageFeaturesDto = {
  brightnessMean: number
  brightnessStdDev: number
  edgeScoreTop: number
  edgeScoreBottom: number
}

export type AutopilotPreviewDto = {
  ok: boolean
  modelId: string
  reason: string | null
  logits: number[] | null
  probabilities: number[] | null
  chosenAction: string | null
  chosenIndex: number | null
  frontUltrasonicM: number | null
  imageFeatures: ImageFeaturesDto | null
  guardReason: string | null
}

export async function fetchAutopilotPreview(clientId: string, runtimeMode: string): Promise<AutopilotPreviewDto> {
  const params = new URLSearchParams({ clientId, runtimeMode })
  const response = await fetch(withBase(`/api/autopilot/preview?${params.toString()}`))
  return handleJson<AutopilotPreviewDto>(response)
}

export async function fetchAutopilotStatus(clientId?: string, runtimeMode?: string): Promise<AutopilotStatusDto> {
  const params = new URLSearchParams()
  if (clientId?.trim()) {
    params.set('clientId', clientId.trim())
  }
  if (runtimeMode?.trim()) {
    params.set('runtimeMode', runtimeMode.trim())
  }
  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  const response = await fetch(withBase(`/api/autopilot/status${suffix}`))
  return handleJson<AutopilotStatusDto>(response)
}

export async function setUltrasonicPosition(
  clientId: string,
  runtimeMode: string,
  angleDeg: number,
  disableAutoScan = true,
  servoPin?: number,
): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({
    clientId,
    runtimeMode,
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

export async function setUltrasonicAutoScan(clientId: string, runtimeMode: string, enabled: boolean): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({ clientId, runtimeMode, enabled: String(enabled) })
  const response = await fetch(withBase(`/api/sensors/ultrasonic/auto-scan?${params.toString()}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledSetPattern(clientId: string, runtimeMode: string, pattern: string): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({ clientId, runtimeMode, pattern })
  const response = await fetch(withBase(`/api/led/pattern?${params.toString()}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledSetCustomFrame(clientId: string, runtimeMode: string, frameHex: string): Promise<SensorBridgeResponse> {
  const params = new URLSearchParams({ clientId, runtimeMode, frameHex })
  const response = await fetch(withBase(`/api/led/custom?${params.toString()}`), { method: 'POST' })
  return handleJson<SensorBridgeResponse>(response)
}

export async function ledClear(clientId: string, runtimeMode: string): Promise<SensorBridgeResponse> {
  const response = await fetch(withBase('/api/led/clear'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId, runtimeMode }),
  })
  return handleJson<SensorBridgeResponse>(response)
}

export function cameraMjpegUrl(clientId: string, runtimeMode: string, agentId?: string): string {
  const params = new URLSearchParams({ clientId, runtimeMode })
  if (agentId?.trim()) {
    params.set('agentId', agentId.trim())
  }

  return withBase(`/api/camera/mjpeg?${params.toString()}`)
}

export async function discoverUnityRuntimes(host?: string, portFrom?: number, portTo?: number): Promise<{
  host: string
  portFrom: number
  portTo: number
  count: number
  instances: Array<{ port: number; host: string; baseUrl: string; healthy: boolean }>
}> {
  const params = new URLSearchParams()
  if (host?.trim()) params.set('host', host.trim())
  if (portFrom !== undefined) params.set('portFrom', String(portFrom))
  if (portTo !== undefined) params.set('portTo', String(portTo))
  const response = await fetch(withBase(`/api/unity/discover?${params.toString()}`))
  return handleJson(response)
}

export function resolveHubUrl(): string {
  return withBase('/hub/telemetry')
}
