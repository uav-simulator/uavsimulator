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
