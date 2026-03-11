import { Alert, Stack } from '@mui/material'
import { LedPanel } from '../components/LedPanel'
import type { SensorTelemetryDto } from '../types'

type Props = {
  runtimeMode: string
  sensorTelemetry: SensorTelemetryDto | null
  onSetPattern: (pattern: string) => Promise<void>
  onSetCustomFrame: (frameHex: string) => Promise<void>
  onClear: () => Promise<void>
}

export function LedPage({ runtimeMode, sensorTelemetry, onSetPattern, onSetCustomFrame, onClear }: Props) {
  if (runtimeMode === 'unity-sim') {
    return (
      <Stack spacing={2}>
        <Alert severity="info">
          LED-панель относится к физическому KS0223 и в текущем `unity-sim` режиме не эмулируется. Для `v1` она
          исключена из обязательного parity-слоя.
        </Alert>
      </Stack>
    )
  }

  return (
    <LedPanel
      sensorTelemetry={sensorTelemetry}
      onSetPattern={onSetPattern}
      onSetCustomFrame={onSetCustomFrame}
      onClear={onClear}
    />
  )
}
