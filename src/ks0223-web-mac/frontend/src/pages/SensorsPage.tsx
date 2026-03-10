import { SensorInventoryPanel } from '../components/SensorInventoryPanel'
import type { CameraStatusDto, SensorBridgeStatusDto, SensorTelemetryDto, StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  camera: CameraStatusDto | null
  sensorStatus: SensorBridgeStatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
}

export function SensorsPage({ status, camera, sensorStatus, sensorTelemetry }: Props) {
  return (
    <SensorInventoryPanel
      status={status}
      camera={camera}
      sensorStatus={sensorStatus}
      sensorTelemetry={sensorTelemetry}
    />
  )
}
