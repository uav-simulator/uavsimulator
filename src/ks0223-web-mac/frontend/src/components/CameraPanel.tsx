import FullscreenExitIcon from '@mui/icons-material/FullscreenExit'
import FullscreenIcon from '@mui/icons-material/Fullscreen'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  FormControl,
  FormControlLabel,
  Grid,
  IconButton,
  InputLabel,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Select,
  Slider,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { CameraStatusDto, HealthDto, SensorTelemetryDto, StatusDto } from '../types'

type OverlayCorner = 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right'

type OverlaySettings = {
  enabled: boolean
  corner: OverlayCorner
  fontSize: number
  bgOpacity: number
  textColor: string
  bgColor: string
  showFrame: boolean
  showTimestamp: boolean
  showTcp: boolean
  showLatency: boolean
  showDistance: boolean
  showScan: boolean
  showTracking: boolean
  showIr: boolean
  showCpuTemp: boolean
  showCameraSource: boolean
  showCameraPosition: boolean
  showDriveSpeed: boolean
  showUltrasonicPosition: boolean
  showControlPad: boolean
  controlPadScale: number
  customLabel: string
}

type Props = {
  cameraStreamUrl: string
  health: HealthDto | null
  camera: CameraStatusDto | null
  status: StatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
  driveSpeedPercent: number
  cameraSpeedPercent: number
  estimatedCameraPanDeg: number
  estimatedCameraTiltDeg: number
  onCommand: (command: string) => Promise<void>
  policyPreview?: {
    ok: boolean
    modelId: string
    reason: string | null
    probabilities: number[] | null
    chosenAction: string | null
    chosenIndex: number | null
    frontUltrasonicM: number | null
    imageFeatures?: {
      brightnessMean: number
      brightnessStdDev: number
      edgeScoreTop: number
      edgeScoreBottom: number
    } | null
    guardReason?: string | null
  } | null
  saliencyEnabled?: boolean
  saliencyClientId?: string
  saliencyRuntimeMode?: string
  autopilotPanel?: ReactNode
  modelVisionPanel?: ReactNode
}

const POLICY_ACTION_NAMES = ['DirStop', 'DirForward', 'DirBack', 'DirLeft', 'DirRight']

const OVERLAY_STORAGE_KEY = 'ks0223_camera_overlay_settings_v2'

const defaultOverlaySettings: OverlaySettings = {
  enabled: true,
  corner: 'top-left',
  fontSize: 14,
  bgOpacity: 0.64,
  textColor: '#dff9ff',
  bgColor: '#0b1726',
  showFrame: true,
  showTimestamp: true,
  showTcp: true,
  showLatency: true,
  showDistance: true,
  showScan: true,
  showTracking: true,
  showIr: true,
  showCpuTemp: true,
  showCameraSource: true,
  showCameraPosition: true,
  showDriveSpeed: true,
  showUltrasonicPosition: true,
  showControlPad: true,
  controlPadScale: 1,
  customLabel: 'KS0223 Overlay',
}

function loadOverlaySettings(): OverlaySettings {
  if (typeof window === 'undefined') {
    return defaultOverlaySettings
  }

  try {
    const raw = window.localStorage.getItem(OVERLAY_STORAGE_KEY)
    if (!raw) {
      return defaultOverlaySettings
    }

    const parsed = JSON.parse(raw) as Partial<OverlaySettings>
    return {
      ...defaultOverlaySettings,
      ...parsed,
      fontSize: clampNumber(parsed.fontSize, 10, 28, defaultOverlaySettings.fontSize),
      bgOpacity: clampNumber(parsed.bgOpacity, 0.05, 0.95, defaultOverlaySettings.bgOpacity),
      controlPadScale: clampNumber(parsed.controlPadScale, 0.75, 1.6, defaultOverlaySettings.controlPadScale),
      customLabel: parsed.customLabel?.slice(0, 48) ?? defaultOverlaySettings.customLabel,
    }
  } catch {
    return defaultOverlaySettings
  }
}

function clampNumber(value: number | undefined, min: number, max: number, fallback: number): number {
  if (typeof value !== 'number' || Number.isNaN(value)) {
    return fallback
  }

  return Math.min(max, Math.max(min, value))
}

function hexToRgba(hex: string, opacity: number): string {
  const normalized = hex.trim().replace('#', '')
  if (!/^[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$/.test(normalized)) {
    return `rgba(11, 23, 38, ${opacity})`
  }

  const expanded = normalized.length === 3 ? normalized.split('').map((char) => `${char}${char}`).join('') : normalized
  const r = Number.parseInt(expanded.slice(0, 2), 16)
  const g = Number.parseInt(expanded.slice(2, 4), 16)
  const b = Number.parseInt(expanded.slice(4, 6), 16)
  return `rgba(${r}, ${g}, ${b}, ${Math.min(0.95, Math.max(0.05, opacity))})`
}

function cleanTelemetryValue(value: string | undefined): string | null {
  if (!value) {
    return null
  }

  const trimmed = value.trim()
  if (!trimmed || trimmed.toLowerCase() === 'null' || trimmed.toLowerCase() === 'undefined') {
    return null
  }

  return trimmed
}

function overlayCornerSx(corner: OverlayCorner): Record<string, string | number> {
  switch (corner) {
    case 'top-right':
      return { top: 12, right: 12 }
    case 'bottom-left':
      return { bottom: 12, left: 12 }
    case 'bottom-right':
      return { bottom: 12, right: 12 }
    default:
      return { top: 12, left: 12 }
  }
}

function clampAngle(value: number): number {
  return Math.max(0, Math.min(180, Math.round(value)))
}

function clampPercent(value: number): number {
  return Math.max(0, Math.min(100, Math.round(value)))
}

function repeatMsForDrive(speedPercent: number): number {
  return Math.max(60, Math.round(220 - clampPercent(speedPercent) * 1.5))
}

function repeatMsForCamera(speedPercent: number): number {
  return Math.max(34, Math.round(190 - clampPercent(speedPercent) * 1.4))
}

export function CameraPanel({
  cameraStreamUrl,
  health,
  camera,
  status,
  sensorTelemetry,
  driveSpeedPercent,
  cameraSpeedPercent,
  estimatedCameraPanDeg,
  estimatedCameraTiltDeg,
  onCommand,
  policyPreview,
  saliencyEnabled = false,
  saliencyClientId = 'web',
  saliencyRuntimeMode = 'real-robot',
  autopilotPanel,
  modelVisionPanel,
}: Props) {
  const hasFrame = (camera?.hasFrame ?? false) || (cameraStreamUrl?.includes('runtimeMode=unity-sim') ?? false)
  const [overlay, setOverlay] = useState<OverlaySettings>(() => loadOverlaySettings())
  const [isFullscreen, setIsFullscreen] = useState(false)
  const viewportRef = useRef<HTMLDivElement | null>(null)

  // Saliency overlay tick — counter that increments at 2 Hz to bust img cache
  const [saliencyTick, setSaliencyTick] = useState(0)
  useEffect(() => {
    if (!saliencyEnabled) return
    const id = window.setInterval(() => setSaliencyTick((n) => n + 1), 500)
    return () => window.clearInterval(id)
  }, [saliencyEnabled])
  const saliencyUrl = saliencyEnabled
    ? `http://127.0.0.1:5288/saliency?clientId=${encodeURIComponent(saliencyClientId)}&runtimeMode=${encodeURIComponent(saliencyRuntimeMode)}&ultrasonic_cm=${policyPreview?.frontUltrasonicM != null ? Math.round(policyPreview.frontUltrasonicM * 100) : 50}&t=${saliencyTick}`
    : ''

  const driveIntervalRef = useRef<number | null>(null)
  const cameraIntervalRef = useRef<number | null>(null)
  const activeDriveCommandRef = useRef<string | null>(null)
  const activeCameraCommandRef = useRef<string | null>(null)

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    window.localStorage.setItem(OVERLAY_STORAGE_KEY, JSON.stringify(overlay))
  }, [overlay])

  useEffect(() => {
    const onChange = () => {
      setIsFullscreen(document.fullscreenElement === viewportRef.current)
    }

    document.addEventListener('fullscreenchange', onChange)
    return () => {
      document.removeEventListener('fullscreenchange', onChange)
    }
  }, [])

  const clearDriveOverlayTimer = useCallback(() => {
    if (driveIntervalRef.current !== null) {
      window.clearInterval(driveIntervalRef.current)
      driveIntervalRef.current = null
    }
  }, [])

  const clearCameraOverlayTimer = useCallback(() => {
    if (cameraIntervalRef.current !== null) {
      window.clearInterval(cameraIntervalRef.current)
      cameraIntervalRef.current = null
    }
  }, [])

  const stopDriveOverlayHold = useCallback(
    async (emitStop: boolean) => {
      clearDriveOverlayTimer()
      activeDriveCommandRef.current = null
      if (emitStop) {
        await onCommand('DirStop')
      }
    },
    [clearDriveOverlayTimer, onCommand],
  )

  const stopCameraOverlayHold = useCallback(
    async (emitStop: boolean) => {
      clearCameraOverlayTimer()
      activeCameraCommandRef.current = null
      if (emitStop) {
        await onCommand('CamStop')
      }
    },
    [clearCameraOverlayTimer, onCommand],
  )

  const startDriveOverlayHold = useCallback(
    async (command: string) => {
      if (!(status?.tcpConnected ?? false)) {
        return
      }

      if (activeDriveCommandRef.current === command) {
        return
      }

      await stopDriveOverlayHold(false)
      activeDriveCommandRef.current = command
      await onCommand(command)

      const intervalMs = repeatMsForDrive(driveSpeedPercent)
      driveIntervalRef.current = window.setInterval(() => {
        if (activeDriveCommandRef.current === command) {
          void onCommand(command)
        }
      }, intervalMs)
    },
    [driveSpeedPercent, onCommand, status?.tcpConnected, stopDriveOverlayHold],
  )

  const startCameraOverlayHold = useCallback(
    async (command: string) => {
      if (!(status?.tcpConnected ?? false)) {
        return
      }

      if (activeCameraCommandRef.current === command) {
        return
      }

      await stopCameraOverlayHold(false)
      activeCameraCommandRef.current = command
      await onCommand(command)

      const intervalMs = repeatMsForCamera(cameraSpeedPercent)
      cameraIntervalRef.current = window.setInterval(() => {
        if (activeCameraCommandRef.current === command) {
          void onCommand(command)
        }
      }, intervalMs)
    },
    [cameraSpeedPercent, onCommand, status?.tcpConnected, stopCameraOverlayHold],
  )

  useEffect(() => {
    const blur = () => {
      void stopDriveOverlayHold(true)
      void stopCameraOverlayHold(true)
    }

    window.addEventListener('blur', blur)
    return () => {
      window.removeEventListener('blur', blur)
      void stopDriveOverlayHold(true)
      void stopCameraOverlayHold(true)
    }
  }, [stopCameraOverlayHold, stopDriveOverlayHold])

  const toggleFullscreen = useCallback(async () => {
    if (!viewportRef.current) {
      return
    }

    if (document.fullscreenElement === viewportRef.current) {
      await document.exitFullscreen()
      return
    }

    await viewportRef.current.requestFullscreen()
  }, [])

  // Wrapped in useMemo so its identity is stable across renders when the
  // upstream `sensorTelemetry` object hasn't changed — without this, the
  // `useMemo(overlayLines, [...flat...])` below would recompute every
  // render and the React Compiler would refuse to preserve manual memo.
  const flat = useMemo(() => sensorTelemetry?.flat ?? {}, [sensorTelemetry])
  const ultrasonicServoAngle = cleanTelemetryValue(flat['ultrasonic.scan_servo_angle_deg'])

  const overlayLines = useMemo(() => {
    const lines: string[] = []

    if (overlay.customLabel.trim()) {
      lines.push(overlay.customLabel.trim())
    }

    if (overlay.showTimestamp) {
      const ts = sensorTelemetry?.timestamp ? new Date(sensorTelemetry.timestamp) : new Date()
      lines.push(`time: ${ts.toLocaleTimeString()}`)
    }

    if (overlay.showTcp) {
      lines.push(`tcp: ${status?.tcpConnected ? 'connected' : 'disconnected'}`)
    }

    if (overlay.showLatency) {
      const latency = status?.latencyMs
      lines.push(`latency: ${latency === null || latency === undefined ? 'n/a' : `${Math.round(latency)} ms`}`)
    }

    if (overlay.showDriveSpeed) {
      lines.push(`drive speed: ${clampPercent(driveSpeedPercent)}%`)
      lines.push(`camera speed: ${clampPercent(cameraSpeedPercent)}%`)
    }

    if (overlay.showCameraPosition) {
      lines.push(`camera pan/tilt: ${clampAngle(estimatedCameraPanDeg)} / ${clampAngle(estimatedCameraTiltDeg)} deg`)
    }

    if (overlay.showUltrasonicPosition) {
      lines.push(`ultrasonic servo: ${ultrasonicServoAngle ?? 'n/a'} deg`)
    }

    if (overlay.showDistance) {
      const distance = cleanTelemetryValue(flat['ultrasonic.distance_cm'])
      lines.push(`distance: ${distance ? `${distance} cm` : 'n/a'}`)
    }

    if (overlay.showScan) {
      const left = cleanTelemetryValue(flat['ultrasonic.scan.left_cm']) ?? '-'
      const center = cleanTelemetryValue(flat['ultrasonic.scan.center_cm']) ?? '-'
      const right = cleanTelemetryValue(flat['ultrasonic.scan.right_cm']) ?? '-'
      lines.push(`scan L/C/R: ${left} / ${center} / ${right}`)
    }

    if (overlay.showTracking) {
      const left = cleanTelemetryValue(flat['tracking.left']) ?? '-'
      const center = cleanTelemetryValue(flat['tracking.center']) ?? '-'
      const right = cleanTelemetryValue(flat['tracking.right']) ?? '-'
      lines.push(`tracking L/C/R: ${left} / ${center} / ${right}`)
    }

    if (overlay.showIr) {
      const code = cleanTelemetryValue(flat['ir.last_code_hex']) ?? 'n/a'
      lines.push(`ir: ${code}`)
    }

    if (overlay.showCpuTemp) {
      const temp = cleanTelemetryValue(flat['system.cpu_temp_c'])
      lines.push(`cpu temp: ${temp ? `${temp} C` : 'n/a'}`)
    }

    if (overlay.showCameraSource) {
      lines.push(`camera src: ${camera?.source ?? 'n/a'}`)
    }

    return lines.length > 0 ? lines : ['overlay active']
  }, [
    overlay,
    flat,
    status?.tcpConnected,
    status?.latencyMs,
    // The body reads `sensorTelemetry?.timestamp`; depending on the whole
    // object lets the React Compiler reconcile its inferred dep set with
    // ours (it walks `sensorTelemetry.flat` through the `flat` memo above).
    sensorTelemetry,
    camera?.source,
    driveSpeedPercent,
    cameraSpeedPercent,
    estimatedCameraPanDeg,
    estimatedCameraTiltDeg,
    ultrasonicServoAngle,
  ])

  const overlayContainerSx = overlayCornerSx(overlay.corner)
  const controlsDisabled = !(status?.tcpConnected ?? false)

  const holdDriveProps = (command: string) => ({
    onPointerDown: () => void startDriveOverlayHold(command),
    onPointerUp: () => void stopDriveOverlayHold(true),
    onPointerCancel: () => void stopDriveOverlayHold(true),
    onPointerLeave: () => void stopDriveOverlayHold(true),
    disabled: controlsDisabled,
    size: 'small' as const,
  })

  const holdCameraProps = (command: string) => ({
    onPointerDown: () => void startCameraOverlayHold(command),
    onPointerUp: () => void stopCameraOverlayHold(true),
    onPointerCancel: () => void stopCameraOverlayHold(true),
    onPointerLeave: () => void stopCameraOverlayHold(true),
    disabled: controlsDisabled,
    size: 'small' as const,
  })

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Typography variant="h6">Камера</Typography>

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap alignItems="center">
            <Chip label={`Health: ${health?.status ?? 'unknown'}`} color={health?.status === 'ok' ? 'success' : 'warning'} />
            <Chip label={`Кадров: ${camera?.framesReceived ?? 0}`} variant="outlined" />
            <Chip label={`UDP: ${camera?.udpListenerEnabled ? `:${camera.udpListenPort}` : 'disabled'}`} variant="outlined" />
            <IconButton onClick={() => void toggleFullscreen()} color="primary" size="small">
              {isFullscreen ? <FullscreenExitIcon /> : <FullscreenIcon />}
            </IconButton>
          </Stack>

          {autopilotPanel ? (
            <Box>{autopilotPanel}</Box>
          ) : null}

          {hasFrame ? (
            <Box
              ref={viewportRef}
              sx={{
                position: 'relative',
                borderRadius: 2,
                overflow: 'hidden',
                border: '1px solid',
                borderColor: 'divider',
                background: '#03070c',
              }}
            >
              <Box
                component="img"
                src={cameraStreamUrl}
                alt="KS0223 camera"
                sx={{
                  width: '100%',
                  minHeight: { xs: 240, md: 320 },
                  maxHeight: isFullscreen ? '100vh' : '70vh',
                  objectFit: 'cover',
                  display: 'block',
                }}
              />

              {overlay.enabled ? (
                <Box
                  sx={{
                    position: 'absolute',
                    pointerEvents: 'none',
                    maxWidth: '92%',
                    ...overlayContainerSx,
                  }}
                >
                  <Box
                    sx={{
                      px: 1.2,
                      py: 0.9,
                      borderRadius: 1.5,
                      border: overlay.showFrame ? '1px solid rgba(145, 206, 255, 0.35)' : 'none',
                      backgroundColor: hexToRgba(overlay.bgColor, overlay.bgOpacity),
                      color: overlay.textColor,
                      fontFamily: '"JetBrains Mono", "SF Mono", Menlo, monospace',
                      fontSize: `${overlay.fontSize}px`,
                      lineHeight: 1.35,
                      letterSpacing: 0.2,
                    }}
                  >
                    {overlayLines.map((line, index) => (
                      <Box key={`${line}-${index}`}>{line}</Box>
                    ))}
                  </Box>
                </Box>
              ) : null}

              {saliencyEnabled ? (
                <Box
                  sx={{
                    position: 'absolute',
                    bottom: 12,
                    left: 12,
                    pointerEvents: 'none',
                    border: '1px solid rgba(255, 184, 108, 0.55)',
                    borderRadius: 1,
                    overflow: 'hidden',
                    background: 'rgba(0,0,0,0.7)',
                    boxShadow: '0 4px 18px rgba(0,0,0,0.4)',
                  }}
                >
                  <Box sx={{ px: 0.8, py: 0.3, color: '#ffb86c', fontSize: 11, fontFamily: 'monospace' }}>
                    🔥 saliency (CNN attention)
                  </Box>
                  <img
                    src={saliencyUrl}
                    alt="saliency"
                    style={{ display: 'block', width: 360, height: 180, imageRendering: 'pixelated', background: '#000' }}
                  />
                </Box>
              ) : null}

              {policyPreview ? (
                <Box
                  sx={{
                    position: 'absolute',
                    pointerEvents: 'none',
                    top: 12,
                    right: 12,
                    width: 220,
                    px: 1.2,
                    py: 0.9,
                    borderRadius: 1.5,
                    border: '1px solid rgba(33, 150, 243, 0.45)',
                    backgroundColor: 'rgba(6, 15, 25, 0.78)',
                    color: '#cfe6ff',
                    fontFamily: '"JetBrains Mono", "SF Mono", Menlo, monospace',
                    fontSize: '11px',
                    lineHeight: 1.35,
                    backdropFilter: 'blur(2px)',
                  }}
                >
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Box sx={{ color: '#7ecbff', fontWeight: 'bold' }}>👁 Shadow</Box>
                    <Box sx={{ color: policyPreview.ok ? '#7fff7f' : '#ff8e8e' }}>
                      {policyPreview.ok ? '✓' : (policyPreview.reason ?? 'fail')}
                    </Box>
                  </Box>
                  {policyPreview.ok && policyPreview.probabilities ? (
                    <>
                      {POLICY_ACTION_NAMES.map((name, i) => {
                        const p = policyPreview.probabilities?.[i] ?? 0
                        const chosen = policyPreview.chosenIndex === i
                        return (
                          <Box key={name} sx={{ display: 'flex', alignItems: 'center', gap: 0.5, my: 0.2 }}>
                            <Box sx={{ width: 64, color: chosen ? '#7fff7f' : '#aac8e0', fontWeight: chosen ? 'bold' : 'normal' }}>
                              {chosen ? '★' : ' '} {name}
                            </Box>
                            <Box sx={{ flex: 1, height: 8, bgcolor: 'rgba(255,255,255,0.08)', borderRadius: 0.5, overflow: 'hidden' }}>
                              <Box sx={{ height: '100%', width: `${(p * 100).toFixed(1)}%`, bgcolor: chosen ? '#7fff7f' : '#4a9eff', transition: 'width 0.15s' }} />
                            </Box>
                            <Box sx={{ width: 36, textAlign: 'right', color: chosen ? '#7fff7f' : '#cfe6ff' }}>
                              {(p * 100).toFixed(0)}%
                            </Box>
                          </Box>
                        )
                      })}
                      {policyPreview.frontUltrasonicM != null ? (
                        <Box sx={{ mt: 0.5, color: '#7ec8ff' }}>
                          sonar: {(policyPreview.frontUltrasonicM * 100).toFixed(0)} cm
                        </Box>
                      ) : null}
                      {policyPreview.imageFeatures ? (
                        <Box sx={{ mt: 0.5, pt: 0.5, borderTop: '1px dashed rgba(126, 200, 255, 0.3)', color: '#cbd5e0', fontSize: '10px', lineHeight: 1.3 }}>
                          <Box sx={{ color: '#7ec8ff', fontWeight: 'bold', mb: 0.2 }}>image CV</Box>
                          <Box>brightness: {policyPreview.imageFeatures.brightnessMean.toFixed(2)} (mean) / {policyPreview.imageFeatures.brightnessStdDev.toFixed(2)} (std)</Box>
                          <Box>edges: {policyPreview.imageFeatures.edgeScoreTop.toFixed(2)} top / {policyPreview.imageFeatures.edgeScoreBottom.toFixed(2)} bot</Box>
                        </Box>
                      ) : null}
                      {policyPreview.guardReason ? (
                        <Box sx={{ mt: 0.5, color: '#ffb86c', fontSize: '10px' }}>
                          ⚠ guard: {policyPreview.guardReason}
                        </Box>
                      ) : null}
                    </>
                  ) : null}
                </Box>
              ) : null}

              {overlay.enabled && overlay.showControlPad ? (
                <Box
                  sx={{
                    position: 'absolute',
                    right: 12,
                    bottom: 12,
                    transform: `scale(${overlay.controlPadScale})`,
                    transformOrigin: 'bottom right',
                    pointerEvents: 'auto',
                  }}
                >
                  <Stack
                    spacing={0.8}
                    sx={{
                      p: 1,
                      borderRadius: 1.5,
                      backgroundColor: 'rgba(6, 15, 25, 0.72)',
                      border: '1px solid rgba(128, 173, 209, 0.3)',
                      backdropFilter: 'blur(3px)',
                    }}
                  >
                    <Button variant="contained" color="error" size="small" onClick={() => void stopDriveOverlayHold(true)}>
                      STOP
                    </Button>

                    <Stack direction="row" spacing={0.8}>
                      <Box>
                        <Typography variant="caption" color="text.secondary">
                          Drive
                        </Typography>
                        <Grid container spacing={0.5} sx={{ width: 112 }}>
                          <Grid size={4} />
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdDriveProps('DirForward')}>
                              ▲
                            </Button>
                          </Grid>
                          <Grid size={4} />
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdDriveProps('DirLeft')}>
                              ◀
                            </Button>
                          </Grid>
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdDriveProps('DirBack')}>
                              ▼
                            </Button>
                          </Grid>
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdDriveProps('DirRight')}>
                              ▶
                            </Button>
                          </Grid>
                        </Grid>
                      </Box>

                      <Box>
                        <Typography variant="caption" color="text.secondary">
                          Cam
                        </Typography>
                        <Grid container spacing={0.5} sx={{ width: 112 }}>
                          <Grid size={4} />
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdCameraProps('CamUp')}>
                              I
                            </Button>
                          </Grid>
                          <Grid size={4} />
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdCameraProps('CamLeft')}>
                              J
                            </Button>
                          </Grid>
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdCameraProps('CamDown')}>
                              K
                            </Button>
                          </Grid>
                          <Grid size={4}>
                            <Button fullWidth variant="outlined" {...holdCameraProps('CamRight')}>
                              L
                            </Button>
                          </Grid>
                        </Grid>
                      </Box>
                    </Stack>
                  </Stack>
                </Box>
              ) : null}
            </Box>
          ) : (
            <Alert severity="info">
              Поток камеры пока не обнаружен. Backend слушает UDP и проверяет типовые HTTP URL на выбранном IP.
            </Alert>
          )}

          {modelVisionPanel ? (
            <Box>{modelVisionPanel}</Box>
          ) : null}

          <Stack spacing={1.5}>
            <Typography variant="subtitle2">Настройки Overlay</Typography>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ xs: 'stretch', sm: 'center' }}>
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.enabled}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                }
                label="Включить overlay"
              />

              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showFrame}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showFrame: event.target.checked }))}
                    size="small"
                  />
                }
                label="Рамка"
              />

              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showControlPad}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showControlPad: event.target.checked }))}
                    size="small"
                  />
                }
                label="ControlPad в кадре"
              />

              <FormControl size="small" sx={{ minWidth: 170 }}>
                <InputLabel id="overlay-corner-label">Позиция</InputLabel>
                <Select
                  labelId="overlay-corner-label"
                  value={overlay.corner}
                  label="Позиция"
                  onChange={(event) =>
                    setOverlay((prev) => ({
                      ...prev,
                      corner: event.target.value as OverlayCorner,
                    }))
                  }
                >
                  <MenuItem value="top-left">Верх слева</MenuItem>
                  <MenuItem value="top-right">Верх справа</MenuItem>
                  <MenuItem value="bottom-left">Низ слева</MenuItem>
                  <MenuItem value="bottom-right">Низ справа</MenuItem>
                </Select>
              </FormControl>

              <TextField
                size="small"
                label="Текст"
                value={overlay.textColor}
                onChange={(event) => setOverlay((prev) => ({ ...prev, textColor: event.target.value }))}
                sx={{ width: 120 }}
                type="color"
              />

              <TextField
                size="small"
                label="Фон"
                value={overlay.bgColor}
                onChange={(event) => setOverlay((prev) => ({ ...prev, bgColor: event.target.value }))}
                sx={{ width: 120 }}
                type="color"
              />
            </Stack>

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
              <Box sx={{ flex: 1 }}>
                <Typography variant="body2" color="text.secondary" gutterBottom>
                  Размер шрифта: {overlay.fontSize}px
                </Typography>
                <Slider
                  min={10}
                  max={28}
                  step={1}
                  value={overlay.fontSize}
                  onChange={(_, value) =>
                    setOverlay((prev) => ({
                      ...prev,
                      fontSize: Array.isArray(value) ? value[0] ?? prev.fontSize : value,
                    }))
                  }
                />
              </Box>
              <Box sx={{ flex: 1 }}>
                <Typography variant="body2" color="text.secondary" gutterBottom>
                  Прозрачность фона: {Math.round(overlay.bgOpacity * 100)}%
                </Typography>
                <Slider
                  min={0.05}
                  max={0.95}
                  step={0.01}
                  value={overlay.bgOpacity}
                  onChange={(_, value) =>
                    setOverlay((prev) => ({
                      ...prev,
                      bgOpacity: Array.isArray(value) ? value[0] ?? prev.bgOpacity : value,
                    }))
                  }
                />
              </Box>
            </Stack>

            <Box>
              <Typography variant="body2" color="text.secondary" gutterBottom>
                Размер mini-control: {overlay.controlPadScale.toFixed(2)}x
              </Typography>
              <Slider
                min={0.75}
                max={1.6}
                step={0.05}
                value={overlay.controlPadScale}
                onChange={(_, value) =>
                  setOverlay((prev) => ({
                    ...prev,
                    controlPadScale: Array.isArray(value) ? value[0] ?? prev.controlPadScale : value,
                  }))
                }
              />
            </Box>

            <Stack direction="row" flexWrap="wrap" useFlexGap spacing={1.2}>
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showTimestamp}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showTimestamp: event.target.checked }))}
                    size="small"
                  />
                }
                label="Time"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showTcp}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showTcp: event.target.checked }))}
                    size="small"
                  />
                }
                label="TCP"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showLatency}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showLatency: event.target.checked }))}
                    size="small"
                  />
                }
                label="Latency"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showDriveSpeed}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showDriveSpeed: event.target.checked }))}
                    size="small"
                  />
                }
                label="Speed"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showCameraPosition}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showCameraPosition: event.target.checked }))}
                    size="small"
                  />
                }
                label="Cam pos"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showUltrasonicPosition}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showUltrasonicPosition: event.target.checked }))}
                    size="small"
                  />
                }
                label="Ultra pos"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showDistance}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showDistance: event.target.checked }))}
                    size="small"
                  />
                }
                label="HC-SR04"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showScan}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showScan: event.target.checked }))}
                    size="small"
                  />
                }
                label="Scan L/C/R"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showTracking}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showTracking: event.target.checked }))}
                    size="small"
                  />
                }
                label="Tracking"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showIr}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showIr: event.target.checked }))}
                    size="small"
                  />
                }
                label="IR"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showCpuTemp}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showCpuTemp: event.target.checked }))}
                    size="small"
                  />
                }
                label="CPU temp"
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={overlay.showCameraSource}
                    onChange={(event) => setOverlay((prev) => ({ ...prev, showCameraSource: event.target.checked }))}
                    size="small"
                  />
                }
                label="Source"
              />
            </Stack>

            <TextField
              size="small"
              label="Заголовок overlay"
              value={overlay.customLabel}
              onChange={(event) =>
                setOverlay((prev) => ({
                  ...prev,
                  customLabel: event.target.value.slice(0, 48),
                }))
              }
              placeholder="KS0223 Overlay"
            />
          </Stack>

          <Typography variant="body2" color="text.secondary">
            Источник: {camera?.source ?? 'n/a'}
          </Typography>

          <Divider />

          <Typography variant="subtitle2">Обнаруженные HTTP camera endpoints</Typography>
          {camera?.httpDiscoveredStreams?.length ? (
            <List dense>
              {camera.httpDiscoveredStreams.map((endpoint) => (
                <ListItem key={endpoint} disablePadding>
                  <ListItemText primary={endpoint} />
                </ListItem>
              ))}
            </List>
          ) : (
            <Typography variant="body2" color="text.secondary">
              Не найдено.
            </Typography>
          )}
        </Stack>
      </CardContent>
    </Card>
  )
}
