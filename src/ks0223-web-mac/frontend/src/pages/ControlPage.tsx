import { Card, CardContent, Grid } from '@mui/material'
import { useState } from 'react'
import { cameraModelViewUrl, type AutopilotPreviewDto } from '../api'
import { AutopilotPanel } from '../components/AutopilotPanel'
import { DemoReplayPanel } from '../components/DemoReplayPanel'
import { CameraPanel } from '../components/CameraPanel'
import { ConnectionCard } from '../components/ConnectionCard'
import { MazeGeneratorPanel } from '../components/MazeGeneratorPanel'
import { MazeMapPanel } from '../components/MazeMapPanel'
import { TelemetryPanel } from '../components/TelemetryPanel'
import type {
  AutopilotStatusDto,
  CameraStatusDto,
  HealthDto,
  IncomingMessageDto,
  ModelBindingDto,
  ModelCatalogEntryDto,
  SensorBridgeStatusDto,
  SensorTelemetryDto,
  StatusDto,
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
  clientInstanceId: string
  targetHost: string
  onTargetHostChange: (value: string) => void
  targetPort: string
  onTargetPortChange: (value: string) => void
  onResetEndpoint: () => void
  onConnect: () => Promise<void>
  onDisconnect: () => Promise<void>
  unityCatalog: UnityRuntimeCatalogDto | null
  unityCatalogBusy: boolean
  unityCameraMode: string
  onUnityCameraModeChange: (value: string) => void
  unityControlAgentId: string
  onUnityControlAgentIdChange: (value: string) => void
  unityCameraAgentId: string
  onUnityCameraAgentIdChange: (value: string) => void
  onUnityCatalogRefresh: () => Promise<void>
  onUnitySelectionSave: (
    trackId: string,
    vehicleId: string,
    agents: Array<{ agentId?: string; vehicleId?: string; isPrimary?: boolean }>,
    applyImmediately: boolean,
    collisionsEnabled?: boolean,
    seeEachOther?: boolean,
  ) => Promise<void>
  unityCollisionsEnabled: boolean
  unitySeeEachOther: boolean
  onCommand: (command: string) => Promise<void>
  controlsEnabled: boolean
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
  modelCatalog: ModelCatalogEntryDto[]
  modelBinding: ModelBindingDto | null
  autopilot: AutopilotStatusDto | null
  onModelBind: (modelId: string) => Promise<void>
  onAutopilotStart: (payload: { agentId?: string; loopIntervalMs?: number }) => Promise<void>
  onAutopilotStop: () => Promise<void>
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
  clientInstanceId,
  targetHost,
  onTargetHostChange,
  targetPort,
  onTargetPortChange,
  onResetEndpoint,
  onConnect,
  onDisconnect,
  unityCatalog,
  unityCatalogBusy,
  unityCameraMode,
  onUnityCameraModeChange,
  unityControlAgentId,
  onUnityControlAgentIdChange,
  unityCameraAgentId,
  onUnityCameraAgentIdChange,
  onUnityCatalogRefresh,
  onUnitySelectionSave,
  unityCollisionsEnabled,
  unitySeeEachOther,
  onCommand,
  cameraStreamUrl,
  driveSpeedPercent,
  cameraSpeedPercent,
  estimatedCameraPanDeg,
  estimatedCameraTiltDeg,
  modelCatalog,
  modelBinding,
  autopilot,
  onModelBind,
  onAutopilotStart,
  onAutopilotStop,
}: Props) {
  const [policyPreview, setPolicyPreview] = useState<AutopilotPreviewDto | null>(null)
  const [saliencyOn, setSaliencyOn] = useState(false)
  const [selectedAutopilotModelId, setSelectedAutopilotModelId] = useState<string | null>(null)
  const selectedTrackId = unityCatalog?.selectedTrackId
  const isMazeSelected = runtimeMode === 'unity-sim' && selectedTrackId === 'track.cardboard_maze.v1'
  const modelVisionAgentId = runtimeMode === 'unity-sim'
    ? autopilot?.agentId || unityControlAgentId || unityCatalog?.selectedControlAgentId || undefined
    : undefined
  const modelVisionUrl = cameraModelViewUrl(clientInstanceId, runtimeMode, modelVisionAgentId)
  const modelVisionReady = runtimeMode === 'unity-sim' && Boolean(
    modelBinding ||
    selectedAutopilotModelId ||
    policyPreview?.modelId ||
    autopilot?.modelId,
  )

  return (
    <Grid container spacing={2.5}>
      <Grid size={{ xs: 12 }}>
        <ConnectionCard
          status={status}
          busy={busy}
          runtimeMode={runtimeMode}
          onRuntimeModeChange={onRuntimeModeChange}
          targetHost={targetHost}
          onTargetHostChange={onTargetHostChange}
          targetPort={targetPort}
          onTargetPortChange={onTargetPortChange}
          onResetEndpoint={onResetEndpoint}
          onConnect={onConnect}
          onDisconnect={onDisconnect}
          unityCatalog={unityCatalog}
          unityCatalogBusy={unityCatalogBusy}
          unityCameraMode={unityCameraMode}
          onUnityCameraModeChange={onUnityCameraModeChange}
          unityControlAgentId={unityControlAgentId}
          onUnityControlAgentIdChange={onUnityControlAgentIdChange}
          unityCameraAgentId={unityCameraAgentId}
          onUnityCameraAgentIdChange={onUnityCameraAgentIdChange}
          onUnityCatalogRefresh={onUnityCatalogRefresh}
          onUnitySelectionSave={onUnitySelectionSave}
          unityCollisionsEnabled={unityCollisionsEnabled}
          unitySeeEachOther={unitySeeEachOther}
        />
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
          policyPreview={policyPreview}
          saliencyEnabled={saliencyOn}
          saliencyClientId={clientInstanceId}
          saliencyRuntimeMode={runtimeMode}
          autopilotPanel={(
            <AutopilotPanel
              embedded
              catalog={modelCatalog}
              binding={modelBinding}
              autopilot={autopilot}
              runtimeMode={runtimeMode}
              clientId={clientInstanceId}
              unityControlAgentId={unityControlAgentId}
              busy={busy}
              onBind={onModelBind}
              onStartAutopilot={onAutopilotStart}
              onStopAutopilot={onAutopilotStop}
              onShadowPreviewUpdate={setPolicyPreview}
              onSelectedModelChange={setSelectedAutopilotModelId}
              saliencyOn={saliencyOn}
              onSaliencyToggle={setSaliencyOn}
            />
          )}
          modelVisionPanel={modelVisionReady ? (
            <MazeMapPanel
              embedded
              sensorTelemetry={sensorTelemetry}
              selectedTrackId={selectedTrackId}
              modelViewUrl={modelVisionUrl}
              modelViewAgentId={modelVisionAgentId}
              policyPreview={policyPreview}
              autopilot={autopilot}
              modelBinding={modelBinding}
              selectedModelId={selectedAutopilotModelId}
            />
          ) : null}
        />
      </Grid>

      {isMazeSelected ? (
        <Grid size={{ xs: 12, md: 6 }}>
          <Card>
            <CardContent>
              <MazeGeneratorPanel
                clientId={clientInstanceId}
                runtimeMode={runtimeMode}
                unityControlAgentId={unityControlAgentId}
                unityVehicleId={unityCatalog?.selectedVehicleId}
                onGenerated={async () => {
                  await onUnityCatalogRefresh()
                }}
              />
            </CardContent>
          </Card>
        </Grid>
      ) : null}

      <Grid size={{ xs: 12, md: isMazeSelected ? 6 : 12 }}>
        <DemoReplayPanel
          clientId={clientInstanceId}
          runtimeMode={runtimeMode}
          agentId={unityControlAgentId}
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
