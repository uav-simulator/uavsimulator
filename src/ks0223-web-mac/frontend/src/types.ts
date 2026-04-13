export type StatusDto = {
  desiredConnection: boolean
  tcpConnected: boolean
  uiConnectedClients: number
  targetHost: string
  targetPort: number
  latencyMs: number | null
  lastError: string | null
  lastTcpMessageAt: string | null
  isLogging: boolean
  currentLogFile: string | null
  hasParsedTelemetry: boolean
  runtimeMode: string
  runtimeLabel: string | null
}

export type CommandResponse = {
  sent: boolean
  error?: string | null
}

export type IncomingMessageDto = {
  timestamp: string
  message: string
  parsedTelemetry?: Record<string, string> | null
}

export type LogState = {
  isLogging: boolean
  currentFile: string | null
}

export type LogFileInfo = {
  name: string
  absolutePath: string
  sizeBytes: number
  lastWriteTimeUtc: string
}

export type CameraStatusDto = {
  udpListenerEnabled: boolean
  udpListenPort: number
  hasFrame: boolean
  lastFrameAt: string | null
  source: string | null
  framesReceived: number
  bytesReceived: number
  httpProbeCandidates: string[]
  httpDiscoveredStreams: string[]
}

export type HealthDto = {
  status: string
  timestamp: string
  version: string
  control: StatusDto
  camera: CameraStatusDto
  sensors: SensorBridgeStatusDto
}

export type SensorBridgeStatusDto = {
  enabled: boolean
  endpointUrl: string | null
  pollIntervalMs: number
  hasTelemetry: boolean
  lastTelemetryAt: string | null
  lastSuccessAt: string | null
  lastError: string | null
  consecutiveFailures: number
}

export type SensorTelemetryDto = {
  timestamp: string
  sourceUrl: string
  rawJson: string
  flat: Record<string, string>
}

export type SensorBridgeResponse = {
  sent: boolean
  statusCode: number
  body: string | null
  error?: string | null
}

export type UnityRuntimeOptionDto = {
  id: string
  displayName: string
}

export type UnityRuntimeAgentDto = {
  agentId: string
  vehicleId: string
  displayName: string
  isPrimary: boolean
}

export type UnityRuntimeAgentSelectionDraft = {
  agentId: string
  vehicleId: string
}

export type UnityRuntimeCatalogDto = {
  selectedTrackId: string
  selectedVehicleId: string
  selectedCameraMode: string
  selectedControlAgentId: string
  selectedCameraAgentId: string
  tracks: UnityRuntimeOptionDto[]
  vehicles: UnityRuntimeOptionDto[]
  agents: UnityRuntimeAgentDto[]
}

export type ModelInfoDto = {
  modelId: string
  name: string
  version: string
  source: string
  createdAtUtc: string
  isActive: boolean
  artifactPath: string
  metadataPath: string
  metricsPath: string
  compatibility: CompatibilityHintsDto
}

export type CompatibilityHintsDto = {
  runtimeModes: string[]
  vehicleIds: string[]
  robotKinds: string[]
}

export type ModelCatalogEntryDto = {
  name: string
  versions: ModelInfoDto[]
}

export type ModelBindingDto = {
  clientId: string
  runtimeMode: string
  agentId: string | null
  modelId: string
  name: string
  version: string
  source: string
  boundAtUtc: string
  compatibility: CompatibilityHintsDto
}

export type AutopilotStatusDto = {
  isRunning: boolean
  clientId: string | null
  runtimeMode: string | null
  agentId: string | null
  modelId: string | null
  startedAtUtc: string | null
  lastStepAtUtc: string | null
  stepsTotal: number
  commandsSent: number
  lastCommand: string | null
  lastThrottle: number
  lastSteer: number
  lastError: string | null
  mode: string
}
