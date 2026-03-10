import { LedPanel } from '../components/LedPanel'
import type { SensorTelemetryDto } from '../types'

type Props = {
  sensorTelemetry: SensorTelemetryDto | null
  onSetPattern: (pattern: string) => Promise<void>
  onSetCustomFrame: (frameHex: string) => Promise<void>
  onClear: () => Promise<void>
}

export function LedPage({ sensorTelemetry, onSetPattern, onSetCustomFrame, onClear }: Props) {
  return (
    <LedPanel
      sensorTelemetry={sensorTelemetry}
      onSetPattern={onSetPattern}
      onSetCustomFrame={onSetCustomFrame}
      onClear={onClear}
    />
  )
}
