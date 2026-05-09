import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import DirectionsCarIcon from '@mui/icons-material/DirectionsCar'
import { AppBar, Box, Container, CssBaseline, Tab, Tabs, Toolbar, Typography } from '@mui/material'
import { createTheme, ThemeProvider } from '@mui/material/styles'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  activateModel,
  cameraMjpegUrl,
  connectPi,
  disconnectPi,
  fetchActiveModel,
  fetchAutopilotStatus,
  fetchCameraStatus,
  fetchHealth,
  fetchLogFiles,
  fetchModelBinding,
  fetchModelCatalog,
  fetchSensorLatest,
  fetchSensorStatus,
  fetchStatus,
  fetchUnityRuntimeCatalog,
  ledClear,
  ledSetCustomFrame,
  ledSetPattern,
  openLogsFolder,
  resolveHubUrl,
  sendCommand,
  setModelBinding,
  startAutopilot,
  setUnityClientSelection,
  setUnityRuntimeSelection,
  stopAutopilot,
  setUltrasonicAutoScan,
  setUltrasonicPosition,
  startLogging,
  stopLogging,
  uploadModelArtifact,
  updateSensorConfig,
} from './api'
import { DemoReplayPanel } from './components/DemoReplayPanel'
import { ScenarioPickerPanel } from './components/ScenarioPickerPanel'
import { ControlPage } from './pages/ControlPage'
import { LedPage } from './pages/LedPage'
import { LogsPage } from './pages/LogsPage'
import { ModelControlPage } from './pages/ModelControlPage'
import { SensorsPage } from './pages/SensorsPage'
import type {
  AutopilotStatusDto,
  CameraStatusDto,
  HealthDto,
  IncomingMessageDto,
  LogFileInfo,
  ModelBindingDto,
  ModelCatalogEntryDto,
  ModelInfoDto,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
  UnityRuntimeCatalogDto,
} from './types'

type TabKey = 'dashboard' | 'sensors' | 'led' | 'logs' | 'models' | 'demoReplay' | 'scenarios'
type UnityAgentDraft = { agentId?: string; vehicleId?: string; isPrimary?: boolean }
type UnityPendingSelection = { trackId: string; vehicleId: string; cameraMode: string; agents: UnityAgentDraft[]; collisionsEnabled: boolean; seeEachOther: boolean }

// `localStorage` key registry and runtime-mode helpers live in their own
// modules so this 1200-line file does not own the source of truth for them.
import { normalizeRuntimeMode, type RuntimeMode } from './runtime-modes'
import {
  CAMERA_PAN_STORAGE_KEY,
  CAMERA_SPEED_STORAGE_KEY,
  CAMERA_TILT_STORAGE_KEY,
  CLIENT_INSTANCE_ID_STORAGE_KEY,
  DRIVE_SPEED_STORAGE_KEY,
  LEGACY_UNITY_SECONDARY_VEHICLE_STORAGE_KEY,
  RUNTIME_MODE_STORAGE_KEY,
  ULTRASONIC_ANGLE_STORAGE_KEY,
  ULTRASONIC_AUTO_SCAN_MIGRATION_V2_KEY,
  ULTRASONIC_AUTO_SCAN_STORAGE_KEY,
  ULTRASONIC_SERVO_PIN_STORAGE_KEY,
  UNITY_CAMERA_AGENT_STORAGE_KEY,
  UNITY_CAMERA_MODE_STORAGE_KEY,
  UNITY_COLLISIONS_ENABLED_STORAGE_KEY,
  UNITY_CONTROL_AGENT_STORAGE_KEY,
  UNITY_SEE_EACH_OTHER_STORAGE_KEY,
  UNITY_TRACK_STORAGE_KEY,
  UNITY_VEHICLE_STORAGE_KEY,
  targetHostStorageKey,
  targetPortStorageKey,
} from './storage-keys'

// Default connection targets per RuntimeMode. `127.0.0.1` for both is the
// safe baseline — operators override the actual robot IP through the
// connection panel (persisted under `targetHostStorageKey(mode)`).
const DEFAULT_TARGET_HOST = '127.0.0.1'
const DEFAULT_UNITY_TARGET_HOST = '127.0.0.1'
const ULTRASONIC_UI_OVERRIDE_MS = 5000

const theme = createTheme({
  palette: {
    mode: 'dark',
    background: {
      default: '#070d12',
      paper: '#101a24',
    },
    primary: {
      main: '#23d5ab',
    },
    secondary: {
      main: '#2ba7ff',
    },
    warning: {
      main: '#f4b957',
    },
  },
  typography: {
    fontFamily: '"Manrope", "Avenir Next", "Segoe UI", sans-serif',
    h4: {
      fontWeight: 700,
      letterSpacing: 0.4,
    },
  },
  shape: {
    borderRadius: 20,
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: {
          background: 'linear-gradient(160deg, rgba(21, 32, 45, 0.9), rgba(14, 24, 35, 0.95))',
          border: '1px solid rgba(85, 125, 155, 0.22)',
          boxShadow: '0 20px 45px rgba(3, 7, 14, 0.45)',
          backdropFilter: 'blur(5px)',
        },
      },
    },
    MuiButton: {
      styleOverrides: {
        root: {
          borderRadius: 12,
          textTransform: 'none',
          fontWeight: 700,
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          fontWeight: 600,
        },
      },
    },
  },
})

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.round(value)))
}

function readStoredNumber(key: string, fallback: number, min: number, max: number): number {
  if (typeof window === 'undefined') {
    return fallback
  }

  const raw = window.localStorage.getItem(key)
  if (!raw) {
    return fallback
  }

  const value = Number(raw)
  if (Number.isNaN(value)) {
    return fallback
  }

  return clamp(value, min, max)
}

function readStoredBool(key: string, fallback: boolean): boolean {
  if (typeof window === 'undefined') {
    return fallback
  }

  const raw = window.localStorage.getItem(key)
  if (raw === null) {
    return fallback
  }

  return raw === 'true'
}

function readStoredString(key: string): string {
  if (typeof window === 'undefined') {
    return ''
  }

  return window.localStorage.getItem(key)?.trim() ?? ''
}

function ensureClientInstanceId(): string {
  if (typeof window === 'undefined') {
    return 'client-server'
  }

  const existing = window.sessionStorage.getItem(CLIENT_INSTANCE_ID_STORAGE_KEY)?.trim()
  if (existing) {
    return existing
  }

  const generated = `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
  window.sessionStorage.setItem(CLIENT_INSTANCE_ID_STORAGE_KEY, generated)
  return generated
}

function defaultHostForMode(mode: RuntimeMode): string {
  return mode === 'unity-sim' ? DEFAULT_UNITY_TARGET_HOST : DEFAULT_TARGET_HOST
}

function defaultPortForMode(mode: RuntimeMode): number {
  return mode === 'unity-sim' ? 8000 : 5051
}

function readStoredTargetHost(mode: RuntimeMode): string {
  if (typeof window === 'undefined') {
    return defaultHostForMode(mode)
  }

  const cached = window.localStorage.getItem(targetHostStorageKey(mode))
  return cached?.trim() || defaultHostForMode(mode)
}

function readStoredTargetPort(mode: RuntimeMode): string {
  if (typeof window === 'undefined') {
    return String(defaultPortForMode(mode))
  }

  const cached = window.localStorage.getItem(targetPortStorageKey(mode))
  const parsed = Number(cached)
  if (!cached || Number.isNaN(parsed) || parsed < 1 || parsed > 65535) {
    return String(defaultPortForMode(mode))
  }

  return String(parsed)
}

function normalizePortInput(value: string, mode: RuntimeMode): string {
  const trimmed = value.trim()
  if (!trimmed) {
    return String(defaultPortForMode(mode))
  }

  const parsed = Number(trimmed)
  if (Number.isNaN(parsed) || parsed < 1 || parsed > 65535) {
    return String(defaultPortForMode(mode))
  }

  return String(Math.round(parsed))
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

function App() {
  const clientInstanceId = useMemo(() => ensureClientInstanceId(), [])
  const [status, setStatus] = useState<StatusDto | null>(null)
  const [incoming, setIncoming] = useState<IncomingMessageDto[]>([])
  const [files, setFiles] = useState<LogFileInfo[]>([])
  const [camera, setCamera] = useState<CameraStatusDto | null>(null)
  const [health, setHealth] = useState<HealthDto | null>(null)
  const [sensorStatus, setSensorStatus] = useState<SensorBridgeStatusDto | null>(null)
  const [sensorTelemetry, setSensorTelemetry] = useState<SensorTelemetryDto | null>(null)
  const [modelCatalog, setModelCatalog] = useState<ModelCatalogEntryDto[]>([])
  const [activeModel, setActiveModel] = useState<ModelInfoDto | null>(null)
  const [modelBinding, setModelBindingState] = useState<ModelBindingDto | null>(null)
  const [autopilotStatus, setAutopilotStatus] = useState<AutopilotStatusDto | null>(null)
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState<TabKey>('dashboard')
  const [selectedRuntimeMode, setSelectedRuntimeMode] = useState<RuntimeMode>(() => {
    if (typeof window === 'undefined') {
      return 'real-robot'
    }

    return normalizeRuntimeMode(window.localStorage.getItem(RUNTIME_MODE_STORAGE_KEY))
  })
  const [targetHost, setTargetHost] = useState(() => readStoredTargetHost(selectedRuntimeMode))
  const [targetPort, setTargetPort] = useState(() => readStoredTargetPort(selectedRuntimeMode))
  const [unityCatalog, setUnityCatalog] = useState<UnityRuntimeCatalogDto | null>(null)
  const [unityCatalogBusy, setUnityCatalogBusy] = useState(false)
  const [unityTrackId, setUnityTrackId] = useState(() => readStoredString(UNITY_TRACK_STORAGE_KEY))
  const [unityVehicleId, setUnityVehicleId] = useState(() => readStoredString(UNITY_VEHICLE_STORAGE_KEY))
  const [unityCameraMode, setUnityCameraMode] = useState(() => readStoredString(UNITY_CAMERA_MODE_STORAGE_KEY) || 'spectator')
  const [unityControlAgentId, setUnityControlAgentId] = useState(() => readStoredString(UNITY_CONTROL_AGENT_STORAGE_KEY))
  const [unityCameraAgentId, setUnityCameraAgentId] = useState(() => readStoredString(UNITY_CAMERA_AGENT_STORAGE_KEY))
  const [unityCollisionsEnabled, setUnityCollisionsEnabled] = useState(() => readStoredBool(UNITY_COLLISIONS_ENABLED_STORAGE_KEY, false))
  const [unitySeeEachOther, setUnitySeeEachOther] = useState(() => readStoredBool(UNITY_SEE_EACH_OTHER_STORAGE_KEY, true))
  const [unityPendingSelection, setUnityPendingSelection] = useState<UnityPendingSelection | null>(null)

  const [driveSpeedPercent, setDriveSpeedPercent] = useState(() => readStoredNumber(DRIVE_SPEED_STORAGE_KEY, 80, 0, 100))
  const [cameraSpeedPercent, setCameraSpeedPercent] = useState(() => readStoredNumber(CAMERA_SPEED_STORAGE_KEY, 70, 0, 100))
  const [ultrasonicAngleDeg, setUltrasonicAngleDeg] = useState(() => readStoredNumber(ULTRASONIC_ANGLE_STORAGE_KEY, 90, 0, 180))
  const [ultrasonicServoPin, setUltrasonicServoPin] = useState(() => readStoredNumber(ULTRASONIC_SERVO_PIN_STORAGE_KEY, 5, 5, 7))
  const [ultrasonicAutoScanEnabled, setUltrasonicAutoScanEnabled] = useState(() => {
    if (typeof window === 'undefined') {
      return false
    }

    const migrated = window.localStorage.getItem(ULTRASONIC_AUTO_SCAN_MIGRATION_V2_KEY) === '1'
    if (!migrated) {
      window.localStorage.setItem(ULTRASONIC_AUTO_SCAN_STORAGE_KEY, 'false')
      window.localStorage.setItem(ULTRASONIC_AUTO_SCAN_MIGRATION_V2_KEY, '1')
      return false
    }

    return readStoredBool(ULTRASONIC_AUTO_SCAN_STORAGE_KEY, false)
  })
  const [estimatedCameraPanDeg, setEstimatedCameraPanDeg] = useState(() => readStoredNumber(CAMERA_PAN_STORAGE_KEY, 90, 0, 180))
  const [estimatedCameraTiltDeg, setEstimatedCameraTiltDeg] = useState(() => readStoredNumber(CAMERA_TILT_STORAGE_KEY, 90, 0, 180))
  const ultrasonicUiOverrideUntilRef = useRef(0)
  const selectedRuntimeModeRef = useRef<RuntimeMode>(selectedRuntimeMode)

  const markUltrasonicUiOverride = useCallback((durationMs = ULTRASONIC_UI_OVERRIDE_MS) => {
    ultrasonicUiOverrideUntilRef.current = Date.now() + Math.max(300, durationMs)
  }, [])

  const activeRuntimeMode = selectedRuntimeMode

  const syncStatus = useCallback(async () => {
    const next = await fetchStatus(clientInstanceId, selectedRuntimeMode)
    setStatus(next)
  }, [clientInstanceId, selectedRuntimeMode])

  const syncFiles = useCallback(async () => {
    const nextFiles = await fetchLogFiles()
    setFiles(nextFiles)
  }, [])

  const syncDiagnostics = useCallback(async () => {
    const [nextHealth, nextCamera, nextSensorStatus, nextSensorTelemetry] = await Promise.all([
      fetchHealth(clientInstanceId, selectedRuntimeMode),
      fetchCameraStatus(clientInstanceId, selectedRuntimeMode),
      fetchSensorStatus(clientInstanceId, selectedRuntimeMode),
      fetchSensorLatest(clientInstanceId, selectedRuntimeMode),
    ])
    setHealth(nextHealth)
    setCamera(nextCamera)
    setSensorStatus(nextSensorStatus)
    setSensorTelemetry(nextSensorTelemetry)
  }, [clientInstanceId, selectedRuntimeMode])

  const syncModelControl = useCallback(async () => {
    const targetAgentId = selectedRuntimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined
    const [nextCatalog, nextActiveModel, nextBinding, nextAutopilot] = await Promise.all([
      fetchModelCatalog(),
      fetchActiveModel(),
      fetchModelBinding(clientInstanceId, selectedRuntimeMode, targetAgentId),
      fetchAutopilotStatus(clientInstanceId, selectedRuntimeMode),
    ])
    setModelCatalog(nextCatalog)
    setActiveModel(nextActiveModel)
    setModelBindingState(nextBinding)
    setAutopilotStatus(nextAutopilot)
  }, [clientInstanceId, selectedRuntimeMode, unityControlAgentId])

  const syncUnityCatalog = useCallback(
    async (hostOverride?: string, portOverride?: number) => {
      const host = hostOverride?.trim() || targetHost.trim()
      const port = portOverride ?? Number(normalizePortInput(targetPort, 'unity-sim'))
      if (!host) {
        throw new Error('Unity host пустой')
      }

      setUnityCatalogBusy(true)
      try {
        const catalog = await fetchUnityRuntimeCatalog(clientInstanceId, 'unity-sim', host, port)
        setUnityCatalog(catalog)
        setUnityTrackId(catalog.selectedTrackId)
        setUnityVehicleId(catalog.selectedVehicleId)
        setUnityCameraMode(catalog.selectedCameraMode || 'spectator')
        setUnityControlAgentId(catalog.selectedControlAgentId || '')
        setUnityCameraAgentId(catalog.selectedCameraAgentId || catalog.selectedControlAgentId || '')
        return catalog
      } finally {
        setUnityCatalogBusy(false)
      }
    },
    [clientInstanceId, targetHost, targetPort],
  )

  const applyUnitySelection = useCallback(
    async (
      trackId: string,
      vehicleId: string,
      agents: UnityAgentDraft[],
      applyImmediately: boolean,
      collisionsEnabled?: boolean,
      seeEachOther?: boolean,
    ) => {
      const effectiveCollisions = collisionsEnabled ?? unityCollisionsEnabled
      const effectiveSeeEachOther = seeEachOther ?? unitySeeEachOther

      if (collisionsEnabled !== undefined) setUnityCollisionsEnabled(collisionsEnabled)
      if (seeEachOther !== undefined) setUnitySeeEachOther(seeEachOther)

      if (!applyImmediately || !(status?.desiredConnection ?? false)) {
        setUnityTrackId(trackId)
        setUnityVehicleId(vehicleId)
        setUnityPendingSelection({ trackId, vehicleId, cameraMode: unityCameraMode, agents, collisionsEnabled: effectiveCollisions, seeEachOther: effectiveSeeEachOther })
        setUnityCatalog((prev) => {
          if (!prev) {
            return prev
          }

          return {
            ...prev,
            selectedTrackId: trackId,
            selectedVehicleId: vehicleId,
            selectedCameraMode: unityCameraMode,
            agents: agents.map((agent, index) => ({
              agentId: agent.agentId?.trim() || `agent-${index + 1}`,
              vehicleId: agent.vehicleId?.trim() || vehicleId,
              displayName:
                prev.vehicles.find((item) => item.id === (agent.vehicleId?.trim() || vehicleId))?.displayName ||
                agent.vehicleId?.trim() ||
                vehicleId,
              isPrimary: Boolean(agent.isPrimary),
            })),
          }
        })
        return
      }

      const catalog = await setUnityRuntimeSelection({
        clientId: clientInstanceId,
        runtimeMode: 'unity-sim',
        trackId,
        vehicleId,
        cameraMode: unityCameraMode,
        agents,
        applyImmediately,
        collisionsEnabled: effectiveCollisions,
        seeEachOther: effectiveSeeEachOther,
      })

      setUnityCatalog(catalog)
      setUnityTrackId(catalog.selectedTrackId)
      setUnityVehicleId(catalog.selectedVehicleId)
      setUnityCameraMode(catalog.selectedCameraMode || 'spectator')
      setUnityControlAgentId(catalog.selectedControlAgentId || '')
      setUnityCameraAgentId(catalog.selectedCameraAgentId || catalog.selectedControlAgentId || '')
      setUnityPendingSelection(null)

      if (applyImmediately) {
        await Promise.all([syncStatus(), syncDiagnostics()])
      }
    },
    [clientInstanceId, status?.desiredConnection, syncDiagnostics, syncStatus, unityCameraMode, unityCollisionsEnabled, unitySeeEachOther],
  )

  useEffect(() => {
    void syncStatus()
    void syncFiles()
    void syncDiagnostics()
    void syncModelControl()

    const hub = new HubConnectionBuilder()
      .withUrl(resolveHubUrl())
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    hub.on('status', (payload: StatusDto) => {
      if (normalizeRuntimeMode(payload.runtimeMode) !== selectedRuntimeModeRef.current) {
        return
      }
      setStatus(payload)
    })

    hub.on('incoming', (message: IncomingMessageDto) => {
      setIncoming((prev) => [message, ...prev].slice(0, 200))
    })

    hub.on('sensorStatus', (payload: SensorBridgeStatusDto) => {
      setSensorStatus(payload)
    })

    hub.on('sensorTelemetry', (payload: SensorTelemetryDto) => {
      setSensorTelemetry(payload)
    })

    void hub
      .start()
      .then(() => hub.invoke('BindClient', clientInstanceId))
      .catch((error: unknown) => {
        console.error('SignalR start error', error)
      })

    return () => {
      void hub.stop()
    }
  }, [clientInstanceId, syncDiagnostics, syncFiles, syncModelControl, syncStatus])

  useEffect(() => {
    const timer = window.setInterval(() => {
      void syncDiagnostics()
    }, 5000)

    return () => {
      window.clearInterval(timer)
    }
  }, [syncDiagnostics])

  useEffect(() => {
    const timer = window.setInterval(() => {
      void syncModelControl()
    }, 4000)

    return () => {
      window.clearInterval(timer)
    }
  }, [syncModelControl])

  useEffect(() => {
    window.localStorage.setItem(RUNTIME_MODE_STORAGE_KEY, selectedRuntimeMode)
    selectedRuntimeModeRef.current = selectedRuntimeMode
  }, [selectedRuntimeMode])

  useEffect(() => {
    const normalized = targetHost.trim()
    if (!normalized) {
      return
    }

    window.localStorage.setItem(targetHostStorageKey(selectedRuntimeMode), normalized)
  }, [selectedRuntimeMode, targetHost])

  useEffect(() => {
    const normalized = normalizePortInput(targetPort, selectedRuntimeMode)
    window.localStorage.setItem(targetPortStorageKey(selectedRuntimeMode), normalized)
  }, [selectedRuntimeMode, targetPort])

  useEffect(() => {
    if (!unityTrackId) {
      window.localStorage.removeItem(UNITY_TRACK_STORAGE_KEY)
      return
    }

    window.localStorage.setItem(UNITY_TRACK_STORAGE_KEY, unityTrackId)
  }, [unityTrackId])

  useEffect(() => {
    if (!unityVehicleId) {
      window.localStorage.removeItem(UNITY_VEHICLE_STORAGE_KEY)
      return
    }

    window.localStorage.setItem(UNITY_VEHICLE_STORAGE_KEY, unityVehicleId)
  }, [unityVehicleId])

  useEffect(() => {
    window.localStorage.removeItem(LEGACY_UNITY_SECONDARY_VEHICLE_STORAGE_KEY)
  }, [])

  useEffect(() => {
    if (!unityCameraMode) {
      window.localStorage.removeItem(UNITY_CAMERA_MODE_STORAGE_KEY)
      return
    }

    window.localStorage.setItem(UNITY_CAMERA_MODE_STORAGE_KEY, unityCameraMode)
  }, [unityCameraMode])

  useEffect(() => {
    if (!unityControlAgentId) {
      window.localStorage.removeItem(UNITY_CONTROL_AGENT_STORAGE_KEY)
      return
    }

    window.localStorage.setItem(UNITY_CONTROL_AGENT_STORAGE_KEY, unityControlAgentId)
  }, [unityControlAgentId])

  useEffect(() => {
    if (!unityCameraAgentId) {
      window.localStorage.removeItem(UNITY_CAMERA_AGENT_STORAGE_KEY)
      return
    }

    window.localStorage.setItem(UNITY_CAMERA_AGENT_STORAGE_KEY, unityCameraAgentId)
  }, [unityCameraAgentId])

  useEffect(() => {
    window.localStorage.setItem(UNITY_COLLISIONS_ENABLED_STORAGE_KEY, String(unityCollisionsEnabled))
  }, [unityCollisionsEnabled])

  useEffect(() => {
    window.localStorage.setItem(UNITY_SEE_EACH_OTHER_STORAGE_KEY, String(unitySeeEachOther))
  }, [unitySeeEachOther])

  useEffect(() => {
    window.localStorage.setItem(DRIVE_SPEED_STORAGE_KEY, String(driveSpeedPercent))
  }, [driveSpeedPercent])

  useEffect(() => {
    window.localStorage.setItem(CAMERA_SPEED_STORAGE_KEY, String(cameraSpeedPercent))
  }, [cameraSpeedPercent])

  useEffect(() => {
    window.localStorage.setItem(ULTRASONIC_ANGLE_STORAGE_KEY, String(ultrasonicAngleDeg))
  }, [ultrasonicAngleDeg])

  useEffect(() => {
    window.localStorage.setItem(ULTRASONIC_AUTO_SCAN_STORAGE_KEY, String(ultrasonicAutoScanEnabled))
  }, [ultrasonicAutoScanEnabled])

  useEffect(() => {
    window.localStorage.setItem(ULTRASONIC_SERVO_PIN_STORAGE_KEY, String(ultrasonicServoPin))
  }, [ultrasonicServoPin])

  useEffect(() => {
    window.localStorage.setItem(CAMERA_PAN_STORAGE_KEY, String(estimatedCameraPanDeg))
  }, [estimatedCameraPanDeg])

  useEffect(() => {
    window.localStorage.setItem(CAMERA_TILT_STORAGE_KEY, String(estimatedCameraTiltDeg))
  }, [estimatedCameraTiltDeg])

  useEffect(() => {
    if (!(status?.tcpConnected ?? false)) {
      return
    }

    const timer = window.setTimeout(() => {
      void updateSensorConfig(clientInstanceId, selectedRuntimeMode, {
        autoScanEnabled: ultrasonicAutoScanEnabled,
        driveSpeedPercent,
        cameraSpeedPercent,
        ultrasonicServoPin,
      }).catch((error) => {
        console.warn('Failed to sync speed config to sensor bridge', error)
      })
    }, 220)

    return () => {
      window.clearTimeout(timer)
    }
  }, [
    cameraSpeedPercent,
    clientInstanceId,
    driveSpeedPercent,
    selectedRuntimeMode,
    status?.tcpConnected,
    ultrasonicAutoScanEnabled,
    ultrasonicServoPin,
  ])

  useEffect(() => {
    const uiOverrideActive = Date.now() < ultrasonicUiOverrideUntilRef.current
    const angle = sensorTelemetry?.flat['ultrasonic.scan_servo_angle_deg']
    if (angle !== undefined && !uiOverrideActive) {
      const parsed = Number(angle)
      if (!Number.isNaN(parsed)) {
        setUltrasonicAngleDeg(clamp(parsed, 0, 180))
      }
    }

    const autoScanRaw = sensorTelemetry?.flat['config.auto_scan_enabled']
    if (autoScanRaw !== undefined && !uiOverrideActive) {
      setUltrasonicAutoScanEnabled(autoScanRaw === 'true' || autoScanRaw === '1')
    }

    const servoPinRaw = sensorTelemetry?.flat['config.ultrasonic_servo_pin']
    if (servoPinRaw !== undefined) {
      const parsed = Number(servoPinRaw)
      if (!Number.isNaN(parsed) && [5, 6, 7].includes(parsed)) {
        setUltrasonicServoPin(parsed)
      }
    }
  }, [sensorTelemetry])

  const guarded = useCallback(async (action: () => Promise<void>) => {
    setBusy(true)
    try {
      await action()
    } finally {
      setBusy(false)
    }
  }, [])

  const waitForConnectionOutcome = useCallback(
    async (mode: RuntimeMode, timeoutMs = 4500) => {
      const startedAt = Date.now()
      while (Date.now() - startedAt < timeoutMs) {
        const next = await fetchStatus(clientInstanceId, mode)
        setStatus(next)
        if (next.tcpConnected || next.lastError) {
          return next
        }

        await sleep(250)
      }

      return null
    },
    [clientInstanceId],
  )

  const handleConnect = useCallback(async () => {
    await guarded(async () => {
      const normalizedHost = targetHost.trim()
      if (!normalizedHost) {
        console.warn('IP/host не может быть пустым')
        return
      }

      const normalizedPort = normalizePortInput(targetPort, selectedRuntimeMode)
      const next = await connectPi(clientInstanceId, selectedRuntimeMode, normalizedHost, Number(normalizedPort))
      setStatus(next)
      await waitForConnectionOutcome(selectedRuntimeMode)

      if (selectedRuntimeMode === 'unity-sim') {
        const catalog = await syncUnityCatalog(normalizedHost, Number(normalizedPort))
        if (unityPendingSelection) {
          await applyUnitySelection(
            unityPendingSelection.trackId || catalog.selectedTrackId,
            unityPendingSelection.vehicleId || catalog.selectedVehicleId,
            unityPendingSelection.agents,
            true,
            unityPendingSelection.collisionsEnabled,
            unityPendingSelection.seeEachOther,
          )
        }
      }
    })
  }, [
    applyUnitySelection,
    clientInstanceId,
    guarded,
    selectedRuntimeMode,
    syncUnityCatalog,
    targetHost,
    targetPort,
    unityPendingSelection,
    waitForConnectionOutcome,
  ])

  const handleDisconnect = useCallback(async () => {
    await guarded(async () => {
      const next = await disconnectPi(clientInstanceId, selectedRuntimeMode)
      setStatus(next)
    })
  }, [clientInstanceId, guarded, selectedRuntimeMode])

  const handleRuntimeModeChange = useCallback((value: string) => {
    const nextMode = normalizeRuntimeMode(value)
    setSelectedRuntimeMode(nextMode)
    setTargetHost(readStoredTargetHost(nextMode))
    setTargetPort(readStoredTargetPort(nextMode))
  }, [])

  const handleResetEndpoint = useCallback(() => {
    setTargetHost(defaultHostForMode(selectedRuntimeMode))
    setTargetPort(String(defaultPortForMode(selectedRuntimeMode)))
  }, [selectedRuntimeMode])

  const applyUnityClientSelection = useCallback(
    async (controlAgentId?: string, cameraAgentId?: string) => {
      if (selectedRuntimeMode !== 'unity-sim') {
        return
      }

      if (!status?.desiredConnection) {
        setUnityControlAgentId(controlAgentId ?? '')
        setUnityCameraAgentId(cameraAgentId ?? controlAgentId ?? '')
        return
      }

      const catalog = await setUnityClientSelection({
        clientId: clientInstanceId,
        runtimeMode: 'unity-sim',
        controlAgentId,
        cameraAgentId,
      })
      setUnityCatalog(catalog)
      setUnityControlAgentId(catalog.selectedControlAgentId || '')
      setUnityCameraAgentId(catalog.selectedCameraAgentId || catalog.selectedControlAgentId || '')
    },
    [clientInstanceId, selectedRuntimeMode, status?.desiredConnection],
  )

  const handleUnityControlAgentChange = useCallback(
    (value: string) => {
      const normalized = value.trim()
      setUnityControlAgentId(normalized)
      void applyUnityClientSelection(normalized || undefined, unityCameraAgentId || undefined).catch((error) => {
        console.warn('Failed to apply unity control agent selection', error)
      })
    },
    [applyUnityClientSelection, unityCameraAgentId],
  )

  const handleUnityCameraAgentChange = useCallback(
    (value: string) => {
      const normalized = value.trim()
      setUnityCameraAgentId(normalized)
      setUnityControlAgentId(normalized)
      void applyUnityClientSelection(normalized || undefined, normalized || undefined).catch((error) => {
        console.warn('Failed to apply unity camera agent selection', error)
      })
    },
    [applyUnityClientSelection],
  )

  const handleCommand = useCallback(
    async (command: string, agentId?: string) => {
      try {
        await sendCommand(clientInstanceId, selectedRuntimeMode, command, agentId)

        if (command === 'CamUp') {
          setEstimatedCameraTiltDeg((prev) => clamp(prev - 1, 0, 180))
        } else if (command === 'CamDown') {
          setEstimatedCameraTiltDeg((prev) => clamp(prev + 1, 0, 180))
        } else if (command === 'CamLeft') {
          setEstimatedCameraPanDeg((prev) => clamp(prev + 1, 0, 180))
        } else if (command === 'CamRight') {
          setEstimatedCameraPanDeg((prev) => clamp(prev - 1, 0, 180))
        }
      } catch (error) {
        console.warn('Command send failed', error)
        await syncStatus()
      }
    },
    [clientInstanceId, selectedRuntimeMode, syncStatus],
  )

  const activeCommandAgentId = activeRuntimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined

  const handleActiveCommand = useCallback(
    async (command: string) => {
      await handleCommand(command, activeCommandAgentId)
    },
    [activeCommandAgentId, handleCommand],
  )

  const handleStartLogging = useCallback(
    async (tag: string) => {
      await guarded(async () => {
        await startLogging(tag)
        await syncStatus()
        await syncFiles()
      })
    },
    [guarded, syncFiles, syncStatus],
  )

  const handleStopLogging = useCallback(async () => {
    await guarded(async () => {
      await stopLogging()
      await syncStatus()
      await syncFiles()
    })
  }, [guarded, syncFiles, syncStatus])

  const handleOpenFolder = useCallback(async () => {
    await openLogsFolder()
  }, [])

  const handleUltrasonicApply = useCallback(
    async (angleDeg: number, disableAutoScan: boolean, servoPin: number) => {
      markUltrasonicUiOverride()
      await setUltrasonicPosition(clientInstanceId, selectedRuntimeMode, angleDeg, disableAutoScan, servoPin)
      setUltrasonicAngleDeg(clamp(angleDeg, 0, 180))
      if ([5, 6, 7].includes(servoPin)) {
        setUltrasonicServoPin(servoPin)
      }
      if (disableAutoScan) {
        setUltrasonicAutoScanEnabled(false)
      }
      await syncDiagnostics()
    },
    [clientInstanceId, markUltrasonicUiOverride, selectedRuntimeMode, syncDiagnostics],
  )

  const handleUltrasonicAutoScanChange = useCallback(
    async (enabled: boolean) => {
      markUltrasonicUiOverride()
      await setUltrasonicAutoScan(clientInstanceId, selectedRuntimeMode, enabled)
      setUltrasonicAutoScanEnabled(enabled)
      await syncDiagnostics()
    },
    [clientInstanceId, markUltrasonicUiOverride, selectedRuntimeMode, syncDiagnostics],
  )

  const handleUltrasonicManualStart = useCallback(async () => {
    markUltrasonicUiOverride()
    if (!ultrasonicAutoScanEnabled) {
      return
    }

    try {
      await setUltrasonicAutoScan(clientInstanceId, selectedRuntimeMode, false)
      setUltrasonicAutoScanEnabled(false)
      markUltrasonicUiOverride()
      void syncDiagnostics()
    } catch (error) {
      console.warn('Failed to disable ultrasonic autoscan before manual control', error)
    }
  }, [clientInstanceId, markUltrasonicUiOverride, selectedRuntimeMode, syncDiagnostics, ultrasonicAutoScanEnabled])

  const handleLedSetPattern = useCallback(
    async (pattern: string) => {
      await ledSetPattern(clientInstanceId, selectedRuntimeMode, pattern)
      await syncDiagnostics()
    },
    [clientInstanceId, selectedRuntimeMode, syncDiagnostics],
  )

  const handleLedSetCustomFrame = useCallback(
    async (frameHex: string) => {
      await ledSetCustomFrame(clientInstanceId, selectedRuntimeMode, frameHex)
      await syncDiagnostics()
    },
    [clientInstanceId, selectedRuntimeMode, syncDiagnostics],
  )

  const handleLedClear = useCallback(async () => {
    await ledClear(clientInstanceId, selectedRuntimeMode)
    await syncDiagnostics()
  }, [clientInstanceId, selectedRuntimeMode, syncDiagnostics])

  const handleModelUpload = useCallback(
    async (payload: {
      file: File
      name?: string
      version?: string
      source?: string
      metadata?: string
      metrics?: string
    }) => {
      await guarded(async () => {
        await uploadModelArtifact(payload)
        await syncModelControl()
      })
    },
    [guarded, syncModelControl],
  )

  const handleModelActivate = useCallback(
    async (modelId: string) => {
      await guarded(async () => {
        await activateModel(modelId)
        await syncModelControl()
      })
    },
    [guarded, syncModelControl],
  )

  const handleModelBind = useCallback(
    async (modelId: string) => {
      await guarded(async () => {
        await setModelBinding({
          clientId: clientInstanceId,
          runtimeMode: selectedRuntimeMode,
          modelId,
          agentId: selectedRuntimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined,
        })
        await syncModelControl()
      })
    },
    [clientInstanceId, guarded, selectedRuntimeMode, syncModelControl, unityControlAgentId],
  )

  const handleAutopilotStart = useCallback(
    async (payload: { agentId?: string; loopIntervalMs?: number }) => {
      await guarded(async () => {
        await startAutopilot({
          clientId: clientInstanceId,
          runtimeMode: selectedRuntimeMode,
          agentId: payload.agentId,
          loopIntervalMs: payload.loopIntervalMs,
        })
        await syncModelControl()
      })
    },
    [clientInstanceId, guarded, selectedRuntimeMode, syncModelControl],
  )

  const handleAutopilotStop = useCallback(async () => {
    await guarded(async () => {
      await stopAutopilot({
        clientId: clientInstanceId,
        runtimeMode: selectedRuntimeMode,
      })
      await syncModelControl()
    })
  }, [clientInstanceId, guarded, selectedRuntimeMode, syncModelControl])

  const content = useMemo(() => {
    if (tab === 'logs') {
      return (
        <LogsPage
          status={status}
          files={files}
          onStart={handleStartLogging}
          onStop={handleStopLogging}
          onOpenFolder={handleOpenFolder}
          onRefresh={syncFiles}
        />
      )
    }

    if (tab === 'sensors') {
      return (
        <SensorsPage
          status={status}
          camera={camera}
          sensorStatus={sensorStatus}
          sensorTelemetry={sensorTelemetry}
        />
      )
    }

    if (tab === 'led') {
      return (
        <LedPage
          runtimeMode={activeRuntimeMode}
          sensorTelemetry={sensorTelemetry}
          onSetPattern={handleLedSetPattern}
          onSetCustomFrame={handleLedSetCustomFrame}
          onClear={handleLedClear}
        />
      )
    }

    if (tab === 'demoReplay') {
      return (
        <DemoReplayPanel
          clientId={clientInstanceId}
          runtimeMode={activeRuntimeMode}
          agentId={activeRuntimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined}
        />
      )
    }

    if (tab === 'scenarios') {
      return <ScenarioPickerPanel />
    }

    if (tab === 'models') {
      return (
        <ModelControlPage
          catalog={modelCatalog}
          activeModel={activeModel}
          binding={modelBinding}
          autopilot={autopilotStatus}
          runtimeMode={activeRuntimeMode}
          unityControlAgentId={unityControlAgentId}
          busy={busy}
          onRefresh={syncModelControl}
          onUpload={handleModelUpload}
          onActivate={handleModelActivate}
          onBind={handleModelBind}
          onStartAutopilot={handleAutopilotStart}
          onStopAutopilot={handleAutopilotStop}
        />
      )
    }

    return (
      <ControlPage
        status={status}
        incoming={incoming}
        camera={camera}
        health={health}
        sensorStatus={sensorStatus}
        sensorTelemetry={sensorTelemetry}
        busy={busy}
        runtimeMode={activeRuntimeMode}
        onRuntimeModeChange={handleRuntimeModeChange}
        clientInstanceId={clientInstanceId}
        targetHost={targetHost}
        onTargetHostChange={setTargetHost}
        targetPort={targetPort}
        onTargetPortChange={setTargetPort}
        onResetEndpoint={handleResetEndpoint}
        onConnect={handleConnect}
        onDisconnect={handleDisconnect}
        unityCatalog={unityCatalog}
        unityCatalogBusy={unityCatalogBusy}
        unityCameraMode={unityCameraMode}
        onUnityCameraModeChange={setUnityCameraMode}
        unityControlAgentId={unityControlAgentId}
        onUnityControlAgentIdChange={handleUnityControlAgentChange}
        unityCameraAgentId={unityCameraAgentId}
        onUnityCameraAgentIdChange={handleUnityCameraAgentChange}
        onUnityCatalogRefresh={async () => {
          await syncUnityCatalog()
        }}
        onUnitySelectionSave={applyUnitySelection}
        unityCollisionsEnabled={unityCollisionsEnabled}
        unitySeeEachOther={unitySeeEachOther}
        onCommand={handleActiveCommand}
        controlsEnabled={activeRuntimeMode !== 'unity-sim' || Boolean(unityControlAgentId)}
        cameraStreamUrl={cameraMjpegUrl(
          clientInstanceId,
          activeRuntimeMode,
          activeRuntimeMode === 'unity-sim' ? unityCameraAgentId || unityControlAgentId || undefined : undefined,
        )}
        driveSpeedPercent={driveSpeedPercent}
        cameraSpeedPercent={cameraSpeedPercent}
        onDriveSpeedPercentChange={(value) => setDriveSpeedPercent(clamp(value, 0, 100))}
        onCameraSpeedPercentChange={(value) => setCameraSpeedPercent(clamp(value, 0, 100))}
        ultrasonicAngleDeg={ultrasonicAngleDeg}
        ultrasonicServoPin={ultrasonicServoPin}
        ultrasonicAutoScanEnabled={ultrasonicAutoScanEnabled}
        onUltrasonicAngleChange={(value) => {
          markUltrasonicUiOverride(2500)
          setUltrasonicAngleDeg(clamp(value, 0, 180))
        }}
        onUltrasonicServoPinChange={(value) => setUltrasonicServoPin([5, 6, 7].includes(value) ? value : 5)}
        onUltrasonicManualStart={handleUltrasonicManualStart}
        onUltrasonicApply={handleUltrasonicApply}
        onUltrasonicAutoScanChange={handleUltrasonicAutoScanChange}
        estimatedCameraPanDeg={estimatedCameraPanDeg}
        estimatedCameraTiltDeg={estimatedCameraTiltDeg}
        modelCatalog={modelCatalog}
        modelBinding={modelBinding}
        autopilot={autopilotStatus}
        onModelBind={handleModelBind}
        onAutopilotStart={handleAutopilotStart}
        onAutopilotStop={handleAutopilotStop}
      />
    )
  }, [
    clientInstanceId,
    tab,
    status,
    incoming,
    camera,
    health,
    sensorStatus,
    sensorTelemetry,
    files,
    modelCatalog,
    modelBinding,
    activeModel,
    autopilotStatus,
    handleStartLogging,
    handleStopLogging,
    handleOpenFolder,
    syncFiles,
    syncModelControl,
    busy,
    activeRuntimeMode,
    targetHost,
    targetPort,
    handleActiveCommand,
    handleResetEndpoint,
    handleRuntimeModeChange,
    handleConnect,
    handleDisconnect,
    unityCatalog,
    unityCatalogBusy,
    unityCameraMode,
    unityControlAgentId,
    unityCameraAgentId,
    unityCollisionsEnabled,
    unitySeeEachOther,
    handleUnityControlAgentChange,
    handleUnityCameraAgentChange,
    syncUnityCatalog,
    applyUnitySelection,
    driveSpeedPercent,
    cameraSpeedPercent,
    ultrasonicAngleDeg,
    ultrasonicServoPin,
    ultrasonicAutoScanEnabled,
    markUltrasonicUiOverride,
    handleUltrasonicManualStart,
    handleUltrasonicApply,
    handleUltrasonicAutoScanChange,
    estimatedCameraPanDeg,
    estimatedCameraTiltDeg,
    handleLedSetPattern,
    handleLedSetCustomFrame,
    handleLedClear,
    handleModelUpload,
    handleModelActivate,
    handleModelBind,
    handleAutopilotStart,
    handleAutopilotStop,
  ])

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Box
        sx={{
          minHeight: '100vh',
          background:
            'radial-gradient(circle at 10% 0%, rgba(35, 213, 171, 0.19), transparent 35%), radial-gradient(circle at 95% 10%, rgba(43, 167, 255, 0.2), transparent 38%), radial-gradient(circle at 50% 120%, rgba(244, 185, 87, 0.15), transparent 40%), #070d12',
        }}
      >
        <AppBar position="static" color="transparent" elevation={0}>
          <Toolbar>
            <DirectionsCarIcon sx={{ mr: 1 }} />
            <Typography variant="h6" sx={{ flexGrow: 1, fontWeight: 800 }}>
              KS0223 Control Center
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 600 }}>
              {activeRuntimeMode === 'unity-sim' ? 'Unity' : 'Pi'} {status?.targetHost ?? targetHost}:{status?.targetPort ?? normalizePortInput(targetPort, activeRuntimeMode)}
            </Typography>
          </Toolbar>
        </AppBar>

        <Container maxWidth="xl" sx={{ py: 2.5 }}>
          <Tabs value={tab} onChange={(_, value: TabKey) => setTab(value)} sx={{ mb: 2.5 }} variant="scrollable">
            <Tab value="dashboard" label="Пульт и телеметрия" />
            <Tab value="scenarios" label="Сценарии" />
            <Tab value="sensors" label="Сенсоры KS0223" />
            <Tab value="led" label="LED панель" />
            <Tab value="models" label="Model Control" />
            <Tab value="demoReplay" label="Demo Replay" />
            <Tab value="logs" label="Логи" />
          </Tabs>

          <Box sx={{ animation: 'fadeIn 0.3s ease-out' }}>{content}</Box>
        </Container>
      </Box>
    </ThemeProvider>
  )
}

export default App
