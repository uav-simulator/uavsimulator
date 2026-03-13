import CameraAltIcon from '@mui/icons-material/CameraAlt'
import RadarIcon from '@mui/icons-material/Radar'
import StopCircleIcon from '@mui/icons-material/StopCircle'
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  Button,
  Card,
  CardContent,
  FormControlLabel,
  Grid,
  Slider,
  Stack,
  Switch,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  onCommand: (command: string) => Promise<void>
  driveSpeedPercent: number
  cameraSpeedPercent: number
  onDriveSpeedPercentChange: (value: number) => void
  onCameraSpeedPercentChange: (value: number) => void
  ultrasonicAngleDeg: number
  ultrasonicServoPin: number
  ultrasonicAutoScanEnabled: boolean
  onUltrasonicAngleChange: (value: number) => void
  onUltrasonicServoPinChange: (value: number) => void
  onUltrasonicManualStart: () => Promise<void>
  onUltrasonicApply: (angleDeg: number, disableAutoScan: boolean, servoPin: number) => Promise<void>
  onUltrasonicAutoScanChange: (enabled: boolean) => Promise<void>
}

const driveKeyToCommand: Record<string, string> = {
  ArrowUp: 'DirForward',
  ArrowDown: 'DirBack',
  ArrowLeft: 'DirLeft',
  ArrowRight: 'DirRight',
  w: 'DirForward',
  a: 'DirLeft',
  s: 'DirBack',
  d: 'DirRight',
}

const cameraKeyToCommand: Record<string, string> = {
  i: 'CamUp',
  k: 'CamDown',
  j: 'CamLeft',
  l: 'CamRight',
}

function resolveDriveCommand(key: string): string | undefined {
  return driveKeyToCommand[key] ?? driveKeyToCommand[key.toLowerCase()]
}

function resolveCameraCommand(key: string): string | undefined {
  return cameraKeyToCommand[key.toLowerCase()]
}

function normalizeKeyId(key: string): string {
  return key.length === 1 ? key.toLowerCase() : key
}

function clampPercent(value: number): number {
  return Math.max(0, Math.min(100, Math.round(value)))
}

function repeatMsForDrive(speedPercent: number): number {
  const speed = clampPercent(speedPercent)
  return Math.max(60, Math.round(220 - speed * 1.5))
}

function repeatMsForCamera(speedPercent: number): number {
  const speed = clampPercent(speedPercent)
  return Math.max(34, Math.round(190 - speed * 1.4))
}

export function ControlPad({
  status,
  onCommand,
  driveSpeedPercent,
  cameraSpeedPercent,
  onDriveSpeedPercentChange,
  onCameraSpeedPercentChange,
  ultrasonicAngleDeg,
  ultrasonicServoPin,
  ultrasonicAutoScanEnabled,
  onUltrasonicAngleChange,
  onUltrasonicServoPinChange,
  onUltrasonicManualStart,
  onUltrasonicApply,
  onUltrasonicAutoScanChange,
}: Props) {
  const driveIntervalRef = useRef<number | null>(null)
  const cameraIntervalRef = useRef<number | null>(null)
  const activeDriveCommandRef = useRef<string | null>(null)
  const activeCameraCommandRef = useRef<string | null>(null)
  const activeDriveKeyRef = useRef<string | null>(null)
  const activeCameraKeyRef = useRef<string | null>(null)
  const driveSpeedRef = useRef(clampPercent(driveSpeedPercent))
  const cameraSpeedRef = useRef(clampPercent(cameraSpeedPercent))
  const ultrasonicManualStartedRef = useRef(false)
  const [ultrasonicBusy, setUltrasonicBusy] = useState(false)

  useEffect(() => {
    driveSpeedRef.current = clampPercent(driveSpeedPercent)
  }, [driveSpeedPercent])

  useEffect(() => {
    cameraSpeedRef.current = clampPercent(cameraSpeedPercent)
  }, [cameraSpeedPercent])

  const clearDriveTimer = useCallback(() => {
    if (driveIntervalRef.current !== null) {
      window.clearInterval(driveIntervalRef.current)
      driveIntervalRef.current = null
    }
  }, [])

  const clearCameraTimer = useCallback(() => {
    if (cameraIntervalRef.current !== null) {
      window.clearInterval(cameraIntervalRef.current)
      cameraIntervalRef.current = null
    }
  }, [])

  const stopDriveHold = useCallback(
    async (emitStop: boolean) => {
      clearDriveTimer()
      activeDriveCommandRef.current = null
      if (emitStop) {
        await onCommand('DirStop')
      }
    },
    [clearDriveTimer, onCommand],
  )

  const stopCameraHold = useCallback(
    async (emitStop: boolean) => {
      clearCameraTimer()
      activeCameraCommandRef.current = null
      if (emitStop) {
        await onCommand('CamStop')
      }
    },
    [clearCameraTimer, onCommand],
  )

  const startDriveHold = useCallback(
    async (command: string) => {
      if (!(status?.tcpConnected ?? false)) {
        return
      }

      if (activeDriveCommandRef.current === command) {
        return
      }

      await stopDriveHold(false)
      activeDriveCommandRef.current = command

      await onCommand(command)
      const intervalMs = repeatMsForDrive(driveSpeedRef.current)
      driveIntervalRef.current = window.setInterval(() => {
        if (activeDriveCommandRef.current === command) {
          void onCommand(command)
        }
      }, intervalMs)
    },
    [onCommand, status?.tcpConnected, stopDriveHold],
  )

  const startCameraHold = useCallback(
    async (command: string) => {
      if (!(status?.tcpConnected ?? false)) {
        return
      }

      if (activeCameraCommandRef.current === command) {
        return
      }

      await stopCameraHold(false)
      activeCameraCommandRef.current = command

      await onCommand(command)
      const intervalMs = repeatMsForCamera(cameraSpeedRef.current)
      cameraIntervalRef.current = window.setInterval(() => {
        if (activeCameraCommandRef.current === command) {
          void onCommand(command)
        }
      }, intervalMs)
    },
    [onCommand, status?.tcpConnected, stopCameraHold],
  )

  useEffect(() => {
    const keyDown = (event: KeyboardEvent) => {
      if (!(status?.tcpConnected ?? false)) {
        return
      }

      const target = event.target as HTMLElement | null
      if (target && ['INPUT', 'TEXTAREA'].includes(target.tagName)) {
        return
      }

      if (event.code === 'Space') {
        event.preventDefault()
        void stopDriveHold(true)
        return
      }

      if (event.key.toLowerCase() === 'x') {
        event.preventDefault()
        void stopCameraHold(true)
        return
      }

      const driveCommand = resolveDriveCommand(event.key)
      if (driveCommand) {
        event.preventDefault()
        activeDriveKeyRef.current = normalizeKeyId(event.key)
        void startDriveHold(driveCommand)
        return
      }

      const cameraCommand = resolveCameraCommand(event.key)
      if (cameraCommand) {
        event.preventDefault()
        activeCameraKeyRef.current = normalizeKeyId(event.key)
        void startCameraHold(cameraCommand)
      }
    }

    const keyUp = (event: KeyboardEvent) => {
      const driveCommand = resolveDriveCommand(event.key)
      const driveKeyId = normalizeKeyId(event.key)
      if (event.code === 'Space' || (driveCommand && activeDriveKeyRef.current === driveKeyId)) {
        event.preventDefault()
        activeDriveKeyRef.current = null
        void stopDriveHold(true)
      }

      const cameraCommand = resolveCameraCommand(event.key)
      const cameraKeyId = normalizeKeyId(event.key)
      if (cameraCommand && activeCameraKeyRef.current === cameraKeyId) {
        event.preventDefault()
        activeCameraKeyRef.current = null
        void stopCameraHold(true)
      }
    }

    const blur = () => {
      activeDriveKeyRef.current = null
      activeCameraKeyRef.current = null
      void stopDriveHold(true)
      void stopCameraHold(true)
    }

    const visibilityChange = () => {
      if (!document.hidden) {
        return
      }

      activeDriveKeyRef.current = null
      activeCameraKeyRef.current = null
      void stopDriveHold(true)
      void stopCameraHold(true)
    }

    window.addEventListener('keydown', keyDown)
    window.addEventListener('keyup', keyUp)
    window.addEventListener('blur', blur)
    document.addEventListener('visibilitychange', visibilityChange)
    return () => {
      window.removeEventListener('keydown', keyDown)
      window.removeEventListener('keyup', keyUp)
      window.removeEventListener('blur', blur)
      document.removeEventListener('visibilitychange', visibilityChange)
      activeDriveKeyRef.current = null
      activeCameraKeyRef.current = null
      void stopDriveHold(true)
      void stopCameraHold(true)
    }
  }, [startCameraHold, startDriveHold, status?.tcpConnected, stopCameraHold, stopDriveHold])

  const disabled = !(status?.tcpConnected ?? false)

  const drivePressProps = (command: string) => ({
    onPointerDown: () => void startDriveHold(command),
    onPointerUp: () => void stopDriveHold(true),
    onPointerCancel: () => void stopDriveHold(true),
    onPointerLeave: () => void stopDriveHold(true),
    disabled,
    size: 'small' as const,
  })

  const cameraPressProps = (command: string) => ({
    onPointerDown: () => void startCameraHold(command),
    onPointerUp: () => void stopCameraHold(true),
    onPointerCancel: () => void stopCameraHold(true),
    onPointerLeave: () => void stopCameraHold(true),
    disabled,
    size: 'small' as const,
  })

  const handleUltrasonicApply = useCallback(
    async (disableAutoScan: boolean) => {
      if (disabled) {
        return
      }

      setUltrasonicBusy(true)
      try {
        await onUltrasonicApply(ultrasonicAngleDeg, disableAutoScan, ultrasonicServoPin)
      } finally {
        setUltrasonicBusy(false)
      }
    },
    [disabled, onUltrasonicApply, ultrasonicAngleDeg, ultrasonicServoPin],
  )

  const handleAutoScanSwitch = useCallback(
    async (enabled: boolean) => {
      if (disabled) {
        return
      }

      setUltrasonicBusy(true)
      try {
        await onUltrasonicAutoScanChange(enabled)
      } finally {
        setUltrasonicBusy(false)
      }
    },
    [disabled, onUltrasonicAutoScanChange],
  )

  const handleUltrasonicSliderChange = useCallback(
    (_: Event, value: number | number[]) => {
      if (!ultrasonicManualStartedRef.current) {
        ultrasonicManualStartedRef.current = true
        void onUltrasonicManualStart()
      }
      onUltrasonicAngleChange(Array.isArray(value) ? value[0] ?? 90 : value)
    },
    [onUltrasonicAngleChange, onUltrasonicManualStart],
  )

  const handleUltrasonicSliderCommit = useCallback(() => {
    ultrasonicManualStartedRef.current = false
    void handleUltrasonicApply(true)
  }, [handleUltrasonicApply])

  return (
    <Card>
      <CardContent sx={{ py: 1.6 }}>
        <Stack spacing={1.6}>
          <Typography variant="h6">Control pad</Typography>
          <Typography variant="body2" color="text.secondary">
            WASD/стрелки = движение, Space = STOP. I/J/K/L = камера, X = CamStop.
          </Typography>

          <Button
            variant="contained"
            color="error"
            size="large"
            startIcon={<StopCircleIcon />}
            onClick={() => void stopDriveHold(true)}
            sx={{ py: 1.15, fontSize: 18, fontWeight: 700 }}
          >
            STOP
          </Button>

          <Grid container spacing={1.5}>
            <Grid size={{ xs: 12, md: 6 }}>
              <Stack spacing={1}>
                <Typography variant="subtitle2">Движение</Typography>
                <Grid container spacing={0.8} sx={{ maxWidth: 280 }}>
                  <Grid size={4} />
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...drivePressProps('DirForward')}>
                      ▲
                    </Button>
                  </Grid>
                  <Grid size={4} />
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...drivePressProps('DirLeft')}>
                      ◀
                    </Button>
                  </Grid>
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...drivePressProps('DirBack')}>
                      ▼
                    </Button>
                  </Grid>
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...drivePressProps('DirRight')}>
                      ▶
                    </Button>
                  </Grid>
                </Grid>
              </Stack>
            </Grid>

            <Grid size={{ xs: 12, md: 6 }}>
              <Stack spacing={1}>
                <Stack direction="row" spacing={0.7} alignItems="center">
                  <CameraAltIcon fontSize="small" />
                  <Typography variant="subtitle2">Камера</Typography>
                  <Button size="small" variant="text" onClick={() => void stopCameraHold(true)} disabled={disabled}>
                    stop
                  </Button>
                </Stack>
                <Grid container spacing={0.8} sx={{ maxWidth: 280 }}>
                  <Grid size={4} />
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...cameraPressProps('CamUp')}>
                      I
                    </Button>
                  </Grid>
                  <Grid size={4} />
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...cameraPressProps('CamLeft')}>
                      J
                    </Button>
                  </Grid>
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...cameraPressProps('CamDown')}>
                      K
                    </Button>
                  </Grid>
                  <Grid size={4}>
                    <Button fullWidth variant="outlined" {...cameraPressProps('CamRight')}>
                      L
                    </Button>
                  </Grid>
                </Grid>
              </Stack>
            </Grid>
          </Grid>

          <Accordion disableGutters sx={{ bgcolor: 'transparent', border: '1px solid rgba(120,140,160,0.22)', borderRadius: 2 }}>
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="subtitle2">Расширенные настройки (скорость и ultrasonic)</Typography>
            </AccordionSummary>
            <AccordionDetails>
              <Stack spacing={2}>
                <Box>
                  <Typography gutterBottom>Скорость машинки (командная): {clampPercent(driveSpeedPercent)}%</Typography>
                  <Tooltip title="Штатный протокол MainControl.py не даёт PWM speed API. Здесь регулируется частота переотправки команды.">
                    <span>
                      <Slider
                        value={clampPercent(driveSpeedPercent)}
                        min={0}
                        max={100}
                        step={1}
                        onChange={(_, value) => onDriveSpeedPercentChange(Array.isArray(value) ? value[0] ?? 0 : value)}
                        disabled={disabled}
                      />
                    </span>
                  </Tooltip>
                </Box>

                <Box>
                  <Typography gutterBottom>Скорость камеры (командная): {clampPercent(cameraSpeedPercent)}%</Typography>
                  <Tooltip title="Регулирует частоту команд CamUp/CamDown/CamLeft/CamRight при удержании.">
                    <span>
                      <Slider
                        value={clampPercent(cameraSpeedPercent)}
                        min={0}
                        max={100}
                        step={1}
                        onChange={(_, value) => onCameraSpeedPercentChange(Array.isArray(value) ? value[0] ?? 0 : value)}
                        disabled={disabled}
                      />
                    </span>
                  </Tooltip>
                </Box>

                <Stack direction="row" alignItems="center" spacing={1}>
                  <RadarIcon fontSize="small" />
                  <Typography variant="subtitle1">HC-SR04 серво</Typography>
                </Stack>

                <Stack spacing={0.5}>
                  <Typography variant="body2" color="text.secondary">
                    GPIO пин сервопривода ultrasonic
                  </Typography>
                  <ToggleButtonGroup
                    exclusive
                    value={ultrasonicServoPin}
                    size="small"
                    onChange={(_, value: number | null) => {
                      if (value !== null) {
                        onUltrasonicServoPinChange(value)
                      }
                    }}
                  >
                    <ToggleButton value={5}>GPIO5</ToggleButton>
                    <ToggleButton value={6}>GPIO6</ToggleButton>
                    <ToggleButton value={7}>GPIO7</ToggleButton>
                  </ToggleButtonGroup>
                </Stack>

                <Box>
                  <Typography gutterBottom>Позиция ультразвукового сенсора: {Math.round(ultrasonicAngleDeg)}°</Typography>
                  <Slider
                    min={0}
                    max={180}
                    step={1}
                    value={Math.round(ultrasonicAngleDeg)}
                    onChange={handleUltrasonicSliderChange}
                    onChangeCommitted={handleUltrasonicSliderCommit}
                    disabled={disabled || ultrasonicBusy}
                  />
                </Box>

                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ xs: 'stretch', sm: 'center' }}>
                  <Button variant="outlined" onClick={() => void handleUltrasonicApply(true)} disabled={disabled || ultrasonicBusy}>
                    Повернуть сенсор
                  </Button>
                  <FormControlLabel
                    control={
                      <Switch
                        checked={ultrasonicAutoScanEnabled}
                        onChange={(event) => void handleAutoScanSwitch(event.target.checked)}
                        disabled={disabled || ultrasonicBusy}
                      />
                    }
                    label="Автоскан"
                  />
                </Stack>
              </Stack>
            </AccordionDetails>
          </Accordion>
        </Stack>
      </CardContent>
    </Card>
  )
}
