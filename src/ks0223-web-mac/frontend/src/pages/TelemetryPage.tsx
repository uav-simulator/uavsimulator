import { Stack } from '@mui/material'
import { cameraMjpegUrl } from '../api'
import { CameraPanel } from '../components/CameraPanel'
import { SensorInventoryPanel } from '../components/SensorInventoryPanel'
import { TelemetryPanel } from '../components/TelemetryPanel'
import type {
  CameraStatusDto,
  HealthDto,
  IncomingMessageDto,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
} from '../types'

type Props = {
  status: StatusDto | null
  incoming: IncomingMessageDto[]
  camera: CameraStatusDto | null
  health: HealthDto | null
  sensorStatus: SensorBridgeStatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
}

export function TelemetryPage({ status, incoming, camera, health, sensorStatus, sensorTelemetry }: Props) {
  return (
    <Stack spacing={2}>
      <CameraPanel
        cameraStreamUrl={cameraMjpegUrl('telemetry-preview', 'real-robot')}
        camera={camera}
        health={health}
        status={status}
        sensorTelemetry={sensorTelemetry}
        driveSpeedPercent={0}
        cameraSpeedPercent={0}
        estimatedCameraPanDeg={90}
        estimatedCameraTiltDeg={90}
        onCommand={async () => {}}
      />
      <SensorInventoryPanel
        status={status}
        camera={camera}
        sensorStatus={sensorStatus}
        sensorTelemetry={sensorTelemetry}
      />
      <TelemetryPanel
        status={status}
        incoming={incoming}
        sensorStatus={sensorStatus}
        sensorTelemetry={sensorTelemetry}
      />
    </Stack>
  )
}
