import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import DirectionsCarIcon from '@mui/icons-material/DirectionsCar'
import { AppBar, Box, Container, CssBaseline, Tab, Tabs, Toolbar, Typography } from '@mui/material'
import { createTheme, ThemeProvider } from '@mui/material/styles'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  connectPi,
  disconnectPi,
  fetchCameraStatus,
  fetchHealth,
  fetchLogFiles,
  fetchSensorLatest,
  fetchSensorStatus,
  fetchStatus,
  ledClear,
  ledSetCustomFrame,
  ledSetPattern,
  openLogsFolder,
  resolveHubUrl,
  sendCommand,
  setUltrasonicAutoScan,
  setUltrasonicPosition,
  startLogging,
  stopLogging,
  updateSensorConfig,
} from './api'
import { ControlPage } from './pages/ControlPage'
import { LedPage } from './pages/LedPage'
import { LogsPage } from './pages/LogsPage'
import { SensorsPage } from './pages/SensorsPage'
import type {
  CameraStatusDto,
  HealthDto,
  IncomingMessageDto,
  LogFileInfo,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
} from './types'

type TabKey = 'dashboard' | 'sensors' | 'led' | 'logs'
const TARGET_HOST_STORAGE_KEY = 'ks0223_target_host'
const DEFAULT_TARGET_HOST = '192.168.1.121'
const DRIVE_SPEED_STORAGE_KEY = 'ks0223_drive_speed_percent'
const CAMERA_SPEED_STORAGE_KEY = 'ks0223_camera_speed_percent'
const ULTRASONIC_ANGLE_STORAGE_KEY = 'ks0223_ultrasonic_angle_deg'
const ULTRASONIC_AUTO_SCAN_STORAGE_KEY = 'ks0223_ultrasonic_auto_scan'
const ULTRASONIC_SERVO_PIN_STORAGE_KEY = 'ks0223_ultrasonic_servo_pin'
const CAMERA_PAN_STORAGE_KEY = 'ks0223_camera_pan_deg'
const CAMERA_TILT_STORAGE_KEY = 'ks0223_camera_tilt_deg'
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

function App() {
  const [status, setStatus] = useState<StatusDto | null>(null)
  const [incoming, setIncoming] = useState<IncomingMessageDto[]>([])
  const [files, setFiles] = useState<LogFileInfo[]>([])
  const [camera, setCamera] = useState<CameraStatusDto | null>(null)
  const [health, setHealth] = useState<HealthDto | null>(null)
  const [sensorStatus, setSensorStatus] = useState<SensorBridgeStatusDto | null>(null)
  const [sensorTelemetry, setSensorTelemetry] = useState<SensorTelemetryDto | null>(null)
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState<TabKey>('dashboard')
  const [targetHost, setTargetHost] = useState(() => {
    if (typeof window === 'undefined') {
      return DEFAULT_TARGET_HOST
    }

    const cached = window.localStorage.getItem(TARGET_HOST_STORAGE_KEY)
    return cached?.trim() || DEFAULT_TARGET_HOST
  })

  const [driveSpeedPercent, setDriveSpeedPercent] = useState(() => readStoredNumber(DRIVE_SPEED_STORAGE_KEY, 80, 0, 100))
  const [cameraSpeedPercent, setCameraSpeedPercent] = useState(() => readStoredNumber(CAMERA_SPEED_STORAGE_KEY, 70, 0, 100))
  const [ultrasonicAngleDeg, setUltrasonicAngleDeg] = useState(() => readStoredNumber(ULTRASONIC_ANGLE_STORAGE_KEY, 90, 0, 180))
  const [ultrasonicServoPin, setUltrasonicServoPin] = useState(() => readStoredNumber(ULTRASONIC_SERVO_PIN_STORAGE_KEY, 5, 5, 7))
  const [ultrasonicAutoScanEnabled, setUltrasonicAutoScanEnabled] = useState(() =>
    readStoredBool(ULTRASONIC_AUTO_SCAN_STORAGE_KEY, true),
  )
  const [estimatedCameraPanDeg, setEstimatedCameraPanDeg] = useState(() => readStoredNumber(CAMERA_PAN_STORAGE_KEY, 90, 0, 180))
  const [estimatedCameraTiltDeg, setEstimatedCameraTiltDeg] = useState(() => readStoredNumber(CAMERA_TILT_STORAGE_KEY, 90, 0, 180))
  const ultrasonicUiOverrideUntilRef = useRef(0)

  const markUltrasonicUiOverride = useCallback((durationMs = ULTRASONIC_UI_OVERRIDE_MS) => {
    ultrasonicUiOverrideUntilRef.current = Date.now() + Math.max(300, durationMs)
  }, [])

  const syncStatus = useCallback(async () => {
    const next = await fetchStatus()
    setStatus(next)
  }, [])

  const syncFiles = useCallback(async () => {
    const nextFiles = await fetchLogFiles()
    setFiles(nextFiles)
  }, [])

  const syncDiagnostics = useCallback(async () => {
    const [nextHealth, nextCamera, nextSensorStatus, nextSensorTelemetry] = await Promise.all([
      fetchHealth(),
      fetchCameraStatus(),
      fetchSensorStatus(),
      fetchSensorLatest(),
    ])
    setHealth(nextHealth)
    setCamera(nextCamera)
    setSensorStatus(nextSensorStatus)
    setSensorTelemetry(nextSensorTelemetry)
  }, [])

  useEffect(() => {
    void syncStatus()
    void syncFiles()
    void syncDiagnostics()

    const hub = new HubConnectionBuilder()
      .withUrl(resolveHubUrl())
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    hub.on('status', (payload: StatusDto) => {
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

    void hub.start().catch((error: unknown) => {
      console.error('SignalR start error', error)
    })

    return () => {
      void hub.stop()
    }
  }, [syncDiagnostics, syncFiles, syncStatus])

  useEffect(() => {
    const timer = window.setInterval(() => {
      void syncDiagnostics()
    }, 5000)

    return () => {
      window.clearInterval(timer)
    }
  }, [syncDiagnostics])

  useEffect(() => {
    const normalized = targetHost.trim()
    if (!normalized) {
      return
    }

    window.localStorage.setItem(TARGET_HOST_STORAGE_KEY, normalized)
  }, [targetHost])

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
    const timer = window.setTimeout(() => {
      void updateSensorConfig({
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
  }, [driveSpeedPercent, cameraSpeedPercent, ultrasonicServoPin])

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

  const handleConnect = useCallback(async () => {
    await guarded(async () => {
      const normalizedHost = targetHost.trim()
      if (!normalizedHost) {
        console.warn('IP/host не может быть пустым')
        return
      }

      const next = await connectPi(normalizedHost)
      setStatus(next)
      setTargetHost(next.targetHost)
      window.localStorage.setItem(TARGET_HOST_STORAGE_KEY, next.targetHost)
    })
  }, [guarded, targetHost])

  const handleDisconnect = useCallback(async () => {
    await guarded(async () => {
      const next = await disconnectPi()
      setStatus(next)
    })
  }, [guarded])

  const handleCommand = useCallback(
    async (command: string) => {
      try {
        await sendCommand(command)

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
    [syncStatus],
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
      await setUltrasonicPosition(angleDeg, disableAutoScan, servoPin)
      setUltrasonicAngleDeg(clamp(angleDeg, 0, 180))
      if ([5, 6, 7].includes(servoPin)) {
        setUltrasonicServoPin(servoPin)
      }
      if (disableAutoScan) {
        setUltrasonicAutoScanEnabled(false)
      }
      await syncDiagnostics()
    },
    [markUltrasonicUiOverride, syncDiagnostics],
  )

  const handleUltrasonicAutoScanChange = useCallback(
    async (enabled: boolean) => {
      markUltrasonicUiOverride()
      await setUltrasonicAutoScan(enabled)
      setUltrasonicAutoScanEnabled(enabled)
      await syncDiagnostics()
    },
    [markUltrasonicUiOverride, syncDiagnostics],
  )

  const handleUltrasonicManualStart = useCallback(async () => {
    markUltrasonicUiOverride()
    if (!ultrasonicAutoScanEnabled) {
      return
    }

    try {
      await setUltrasonicAutoScan(false)
      setUltrasonicAutoScanEnabled(false)
      markUltrasonicUiOverride()
      void syncDiagnostics()
    } catch (error) {
      console.warn('Failed to disable ultrasonic autoscan before manual control', error)
    }
  }, [markUltrasonicUiOverride, syncDiagnostics, ultrasonicAutoScanEnabled])

  const handleLedSetPattern = useCallback(
    async (pattern: string) => {
      await ledSetPattern(pattern)
      await syncDiagnostics()
    },
    [syncDiagnostics],
  )

  const handleLedSetCustomFrame = useCallback(
    async (frameHex: string) => {
      await ledSetCustomFrame(frameHex)
      await syncDiagnostics()
    },
    [syncDiagnostics],
  )

  const handleLedClear = useCallback(async () => {
    await ledClear()
    await syncDiagnostics()
  }, [syncDiagnostics])

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
          sensorTelemetry={sensorTelemetry}
          onSetPattern={handleLedSetPattern}
          onSetCustomFrame={handleLedSetCustomFrame}
          onClear={handleLedClear}
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
        targetHost={targetHost}
        onTargetHostChange={setTargetHost}
        onConnect={handleConnect}
        onDisconnect={handleDisconnect}
        onCommand={handleCommand}
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
      />
    )
  }, [
    tab,
    status,
    incoming,
    camera,
    health,
    sensorStatus,
    sensorTelemetry,
    files,
    handleStartLogging,
    handleStopLogging,
    handleOpenFolder,
    syncFiles,
    busy,
    targetHost,
    handleConnect,
    handleDisconnect,
    handleCommand,
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
              Pi {status?.targetHost ?? targetHost}:{status?.targetPort ?? 5051}
            </Typography>
          </Toolbar>
        </AppBar>

        <Container maxWidth="xl" sx={{ py: 2.5 }}>
          <Tabs value={tab} onChange={(_, value: TabKey) => setTab(value)} sx={{ mb: 2.5 }} variant="scrollable">
            <Tab value="dashboard" label="Пульт и телеметрия" />
            <Tab value="sensors" label="Сенсоры KS0223" />
            <Tab value="led" label="LED панель" />
            <Tab value="logs" label="Логи" />
          </Tabs>

          <Box sx={{ animation: 'fadeIn 0.3s ease-out' }}>{content}</Box>
        </Container>
      </Box>
    </ThemeProvider>
  )
}

export default App
