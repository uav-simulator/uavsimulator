import ModelTrainingIcon from '@mui/icons-material/ModelTraining'
import PlayCircleFilledWhiteIcon from '@mui/icons-material/PlayCircleFilledWhite'
import StopCircleIcon from '@mui/icons-material/StopCircle'
import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useMemo, useState } from 'react'
import type { AutopilotStatusDto, ModelBindingDto, ModelCatalogEntryDto } from '../types'

type Props = {
  catalog: ModelCatalogEntryDto[]
  binding: ModelBindingDto | null
  autopilot: AutopilotStatusDto | null
  runtimeMode: string
  unityControlAgentId: string
  busy: boolean
  onBind: (modelId: string) => Promise<void>
  onStartAutopilot: (payload: { agentId?: string; loopIntervalMs?: number }) => Promise<void>
  onStopAutopilot: () => Promise<void>
}

export function AutopilotPanel({
  catalog,
  binding,
  autopilot,
  runtimeMode,
  unityControlAgentId,
  busy,
  onBind,
  onStartAutopilot,
  onStopAutopilot,
}: Props) {
  const [selectedName, setSelectedName] = useState('')
  const [selectedModelId, setSelectedModelId] = useState('')
  const [loopIntervalMs, setLoopIntervalMs] = useState('140')

  const selectedGroup = useMemo(
    () => catalog.find((item) => item.name === selectedName) ?? null,
    [catalog, selectedName],
  )
  const selectedVersion = useMemo(
    () => selectedGroup?.versions.find((item) => item.modelId === selectedModelId) ?? null,
    [selectedGroup, selectedModelId],
  )

  useEffect(() => {
    if (catalog.length === 0) {
      setSelectedName('')
      setSelectedModelId('')
      return
    }

    const preferredModelId = binding?.modelId ?? catalog[0]?.versions[0]?.modelId ?? ''
    const preferredGroup =
      catalog.find((entry) => entry.versions.some((item) => item.modelId === preferredModelId)) ??
      catalog[0]

    if (!selectedName || !catalog.some((entry) => entry.name === selectedName)) {
      setSelectedName(preferredGroup.name)
    }

    const nextGroup = catalog.find((entry) => entry.name === (selectedName || preferredGroup.name)) ?? preferredGroup
    if (!nextGroup.versions.some((item) => item.modelId === selectedModelId)) {
      const preferredVersion =
        nextGroup.versions.find((item) => item.modelId === preferredModelId) ??
        nextGroup.versions[0] ??
        null
      setSelectedModelId(preferredVersion?.modelId ?? '')
    }
  }, [binding?.modelId, catalog, selectedModelId, selectedName])

  const handleBind = async () => {
    if (!selectedVersion) return
    await onBind(selectedVersion.modelId)
  }

  const handleStart = async () => {
    if (!binding) return
    const parsedLoop = Number(loopIntervalMs)
    await onStartAutopilot({
      agentId: runtimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined,
      loopIntervalMs: Number.isFinite(parsedLoop) && parsedLoop > 0 ? parsedLoop : undefined,
    })
  }

  const isRunning = autopilot?.isRunning ?? false
  const canBind = !busy && Boolean(selectedVersion) && (runtimeMode !== 'unity-sim' || Boolean(unityControlAgentId))
  const canStart = !busy && Boolean(binding) && !isRunning && (runtimeMode !== 'unity-sim' || Boolean(unityControlAgentId))
  const canStop = !busy && isRunning

  return (
    <Card>
      <CardContent>
        <Stack spacing={1.5}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="h6">Autopilot</Typography>
            <Chip
              size="small"
              icon={isRunning ? <PlayCircleFilledWhiteIcon /> : <StopCircleIcon />}
              color={isRunning ? 'success' : 'default'}
              label={isRunning ? 'running' : 'manual'}
            />
          </Box>

          <Typography variant="body2" color="text.secondary">
            Bound: {binding ? `${binding.name} ${binding.version}` : 'не привязана'}
          </Typography>

          <Stack direction="row" spacing={1}>
            <TextField
              select
              size="small"
              label="Model"
              value={selectedName}
              onChange={(event) => {
                const nextName = event.target.value
                setSelectedName(nextName)
                const nextGroup = catalog.find((item) => item.name === nextName)
                setSelectedModelId(nextGroup?.versions[0]?.modelId ?? '')
              }}
              sx={{ flex: 2 }}
              disabled={catalog.length === 0}
            >
              {catalog.map((entry) => (
                <MenuItem key={entry.name} value={entry.name}>
                  {entry.name}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              size="small"
              label="Version"
              value={selectedModelId}
              onChange={(event) => setSelectedModelId(event.target.value)}
              disabled={!selectedGroup}
              sx={{ flex: 1 }}
            >
              {(selectedGroup?.versions ?? []).map((item) => (
                <MenuItem key={item.modelId} value={item.modelId}>
                  {item.version}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              size="small"
              label="Loop ms"
              value={loopIntervalMs}
              onChange={(event) => setLoopIntervalMs(event.target.value)}
              sx={{ width: 90 }}
            />
          </Stack>

          <Stack direction="row" spacing={1} flexWrap="wrap">
            <Button size="small" variant="outlined" disabled={!canBind} onClick={() => void handleBind()}>
              Bind
            </Button>
            <Button
              size="small"
              startIcon={<ModelTrainingIcon />}
              variant="contained"
              color="secondary"
              disabled={!canStart}
              onClick={() => void handleStart()}
            >
              Start
            </Button>
            <Button
              size="small"
              startIcon={<StopCircleIcon />}
              variant="outlined"
              color="warning"
              disabled={!canStop}
              onClick={() => void onStopAutopilot()}
            >
              Stop
            </Button>
          </Stack>

          {autopilot && isRunning ? (
            <Typography variant="caption" color="text.secondary">
              steps={autopilot.stepsTotal} | cmds={autopilot.commandsSent} | throttle={autopilot.lastThrottle.toFixed(2)} steer={autopilot.lastSteer.toFixed(2)}
            </Typography>
          ) : null}
        </Stack>
      </CardContent>
    </Card>
  )
}
