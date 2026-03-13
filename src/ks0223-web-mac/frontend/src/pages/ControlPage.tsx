import { Grid, Stack } from '@mui/material'
import { CameraPanel } from '../components/CameraPanel'
import { ConnectionCard } from '../components/ConnectionCard'
import { ControlPad } from '../components/ControlPad'
import { TelemetryPanel } from '../components/TelemetryPanel'
import type {
  CameraStatusDto,
  HealthDto,
  IncomingMessageDto,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
  UnityRuntimeAgentSelectionDraft,
  UnityRuntimeCatalogDto,
} from '../types'

type Props = {
  status: StatusDto | null
  incoming: IncomingMessageDto[]
  camera: CameraStatusDto | null
  health: HealthDto | null
  sensorStatus: SensorBridgeStatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
  busy: boolean
  runtimeMode: string
  onRuntimeModeChange: (value: string) => void
  targetHost: string
  onTargetHostChange: (value: string) => void
  targetPort: string
  onTargetPortChange: (value: string) => void
  onConnect: () => Promise<void>
  onDisconnect: () => Promise<void>
  unityCatalog: UnityRuntimeCatalogDto | null
  unityCatalogBusy: boolean
  unityCameraMode: string
  onUnityCameraModeChange: (value: string) => void
  unityExtraAgents: UnityRuntimeAgentSelectionDraft[]
  onUnityExtraAgentsChange: (value: UnityRuntimeAgentSelectionDraft[]) => void
  unityControlAgentId: string
  onUnityControlAgentIdChange: (value: string) => void
  unityCameraAgentId: string
  onUnityCameraAgentIdChange: (value: string) => void
  onUnityCatalogRefresh: () => Promise<void>
  onUnitySelectionSave: (trackId: string, vehicleId: string, applyImmediately: boolean) => Promise<void>
  onCommand: (command: string) => Promise<void>
  cameraStreamUrl: string
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
  estimatedCameraPanDeg: number
  estimatedCameraTiltDeg: number
}

export function ControlPage({
  status,
  incoming,
  camera,
  health,
  sensorStatus,
  sensorTelemetry,
  busy,
  runtimeMode,
  onRuntimeModeChange,
  targetHost,
  onTargetHostChange,
  targetPort,
  onTargetPortChange,
  onConnect,
  onDisconnect,
  unityCatalog,
  unityCatalogBusy,
  unityCameraMode,
  onUnityCameraModeChange,
  unityExtraAgents,
  onUnityExtraAgentsChange,
  unityControlAgentId,
  onUnityControlAgentIdChange,
  unityCameraAgentId,
  onUnityCameraAgentIdChange,
  onUnityCatalogRefresh,
  onUnitySelectionSave,
  onCommand,
  cameraStreamUrl,
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
  estimatedCameraPanDeg,
  estimatedCameraTiltDeg,
}: Props) {
  return (
    <Grid container spacing={2.5}>
      <Grid size={{ xs: 12, md: 4 }}>
        <ConnectionCard
          status={status}
          busy={busy}
          runtimeMode={runtimeMode}
          onRuntimeModeChange={onRuntimeModeChange}
          targetHost={targetHost}
          onTargetHostChange={onTargetHostChange}
          targetPort={targetPort}
          onTargetPortChange={onTargetPortChange}
          onConnect={onConnect}
          onDisconnect={onDisconnect}
          unityCatalog={unityCatalog}
          unityCatalogBusy={unityCatalogBusy}
          unityCameraMode={unityCameraMode}
          onUnityCameraModeChange={onUnityCameraModeChange}
          unityExtraAgents={unityExtraAgents}
          onUnityExtraAgentsChange={onUnityExtraAgentsChange}
          unityControlAgentId={unityControlAgentId}
          onUnityControlAgentIdChange={onUnityControlAgentIdChange}
          unityCameraAgentId={unityCameraAgentId}
          onUnityCameraAgentIdChange={onUnityCameraAgentIdChange}
          onUnityCatalogRefresh={onUnityCatalogRefresh}
          onUnitySelectionSave={onUnitySelectionSave}
        />
      </Grid>
      <Grid size={{ xs: 12, md: 8 }}>
        <Stack spacing={2}>
          <ControlPad
            status={status}
            onCommand={onCommand}
            driveSpeedPercent={driveSpeedPercent}
            cameraSpeedPercent={cameraSpeedPercent}
            onDriveSpeedPercentChange={onDriveSpeedPercentChange}
            onCameraSpeedPercentChange={onCameraSpeedPercentChange}
            ultrasonicAngleDeg={ultrasonicAngleDeg}
            ultrasonicServoPin={ultrasonicServoPin}
            ultrasonicAutoScanEnabled={ultrasonicAutoScanEnabled}
            onUltrasonicAngleChange={onUltrasonicAngleChange}
            onUltrasonicServoPinChange={onUltrasonicServoPinChange}
            onUltrasonicManualStart={onUltrasonicManualStart}
            onUltrasonicApply={onUltrasonicApply}
            onUltrasonicAutoScanChange={onUltrasonicAutoScanChange}
          />
        </Stack>
      </Grid>

      <Grid size={{ xs: 12 }}>
        <CameraPanel
          cameraStreamUrl={cameraStreamUrl}
          camera={camera}
          health={health}
          status={status}
          sensorTelemetry={sensorTelemetry}
          driveSpeedPercent={driveSpeedPercent}
          cameraSpeedPercent={cameraSpeedPercent}
          estimatedCameraPanDeg={estimatedCameraPanDeg}
          estimatedCameraTiltDeg={estimatedCameraTiltDeg}
          onCommand={onCommand}
        />
      </Grid>

      <Grid size={{ xs: 12 }}>
        <TelemetryPanel
          status={status}
          incoming={incoming}
          sensorStatus={sensorStatus}
          sensorTelemetry={sensorTelemetry}
        />
      </Grid>
    </Grid>
  )
}
