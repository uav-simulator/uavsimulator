import ModelTrainingIcon from '@mui/icons-material/ModelTraining'
import PlayCircleFilledWhiteIcon from '@mui/icons-material/PlayCircleFilledWhite'
import StopCircleIcon from '@mui/icons-material/StopCircle'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Grid,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useMemo, useState } from 'react'
import type { AutopilotStatusDto, CompatibilityHintsDto, ModelBindingDto, ModelCatalogEntryDto, ModelInfoDto } from '../types'

type Props = {
  catalog: ModelCatalogEntryDto[]
  activeModel: ModelInfoDto | null
  binding: ModelBindingDto | null
  autopilot: AutopilotStatusDto | null
  runtimeMode: string
  unityControlAgentId: string
  busy: boolean
  onRefresh: () => Promise<void>
  onUpload: (payload: {
    file: File
    name?: string
    version?: string
    source?: string
    metadata?: string
    metrics?: string
  }) => Promise<void>
  onActivate: (modelId: string) => Promise<void>
  onBind: (modelId: string) => Promise<void>
  onStartAutopilot: (payload: { agentId?: string; loopIntervalMs?: number }) => Promise<void>
  onStopAutopilot: () => Promise<void>
}

export function ModelControlPage({
  catalog,
  activeModel,
  binding,
  autopilot,
  runtimeMode,
  unityControlAgentId,
  busy,
  onRefresh,
  onUpload,
  onActivate,
  onBind,
  onStartAutopilot,
  onStopAutopilot,
}: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [name, setName] = useState('')
  const [version, setVersion] = useState('')
  const [source, setSource] = useState('python-rl-api')
  const [runtimeModesHint, setRuntimeModesHint] = useState('')
  const [vehicleIdsHint, setVehicleIdsHint] = useState('')
  const [robotKindsHint, setRobotKindsHint] = useState('')
  const [selectedName, setSelectedName] = useState('')
  const [selectedModelId, setSelectedModelId] = useState('')
  const [loopIntervalMs, setLoopIntervalMs] = useState('140')
  const [error, setError] = useState<string | null>(null)

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

    const preferredModelId = binding?.modelId ?? activeModel?.modelId ?? catalog[0]?.versions[0]?.modelId ?? ''
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
  }, [activeModel?.modelId, binding?.modelId, catalog, selectedModelId, selectedName])

  const handleUpload = async () => {
    if (!file) {
      setError('Выбери `.onnx` файл модели')
      return
    }

    const normalizedName = name.trim()
    const normalizedVersion = version.trim()
    if (!normalizedName || !normalizedVersion) {
      setError('Для product-модели нужно явно задать name и version')
      return
    }

    setError(null)
    try {
      const metadata = {
        name: normalizedName,
        version: normalizedVersion,
        source: source.trim() || 'python-rl-api',
        compatibility: {
          runtimeModes: parseCsv(runtimeModesHint),
          vehicleIds: parseCsv(vehicleIdsHint),
          robotKinds: parseCsv(robotKindsHint),
        },
      }

      await onUpload({
        file,
        name: normalizedName,
        version: normalizedVersion,
        source: source.trim() || undefined,
        metadata: JSON.stringify(metadata),
        metrics: JSON.stringify({ note: 'uploaded from web-ui' }),
      })
      setFile(null)
      setName('')
      setVersion('')
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleBind = async () => {
    if (!selectedVersion) {
      setError('Сначала выбери модель и версию')
      return
    }

    if (runtimeMode === 'unity-sim' && !unityControlAgentId) {
      setError('Для unity-sim сначала выбери control agent')
      return
    }

    setError(null)
    try {
      await onBind(selectedVersion.modelId)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleStart = async () => {
    if (!binding) {
      setError('Сначала привяжи модель к текущей цели запуска')
      return
    }

    setError(null)
    try {
      const parsedLoop = Number(loopIntervalMs)
      await onStartAutopilot({
        agentId: runtimeMode === 'unity-sim' ? unityControlAgentId || undefined : undefined,
        loopIntervalMs: Number.isFinite(parsedLoop) && parsedLoop > 0 ? parsedLoop : undefined,
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <Grid container spacing={2.5}>
      <Grid size={{ xs: 12, md: 5 }}>
        <Card>
          <CardContent>
            <Stack spacing={1.5}>
              <Typography variant="h6">Model Upload</Typography>
              <Typography variant="body2" color="text.secondary">
                Устанавливает versioned ONNX-артефакт в backend catalog.
              </Typography>
              <Button component="label" variant="outlined" startIcon={<UploadFileIcon />}>
                {file ? file.name : 'Выбрать .onnx'}
                <input
                  hidden
                  type="file"
                  accept=".onnx"
                  onChange={(event) => {
                    const selected = event.target.files?.[0] ?? null
                    setFile(selected)
                  }}
                />
              </Button>
              <TextField size="small" label="Model name" value={name} onChange={(event) => setName(event.target.value)} />
              <TextField size="small" label="Version" value={version} onChange={(event) => setVersion(event.target.value)} />
              <TextField size="small" label="Source" value={source} onChange={(event) => setSource(event.target.value)} />
              <TextField
                size="small"
                label="Recommended runtime modes"
                placeholder="unity-sim, real-robot"
                value={runtimeModesHint}
                onChange={(event) => setRuntimeModesHint(event.target.value)}
              />
              <TextField
                size="small"
                label="Recommended vehicle IDs"
                placeholder="vehicle.prometeo.sport.v1"
                value={vehicleIdsHint}
                onChange={(event) => setVehicleIdsHint(event.target.value)}
              />
              <TextField
                size="small"
                label="Recommended robot kinds"
                placeholder="ks0223"
                value={robotKindsHint}
                onChange={(event) => setRobotKindsHint(event.target.value)}
              />
              <Stack direction="row" spacing={1}>
                <Button disabled={busy} onClick={() => void onRefresh()} variant="outlined">
                  Refresh
                </Button>
                <Button disabled={busy || !file} onClick={() => void handleUpload()} variant="contained">
                  Install
                </Button>
              </Stack>
            </Stack>
          </CardContent>
        </Card>
      </Grid>

      <Grid size={{ xs: 12, md: 7 }}>
        <Card>
          <CardContent>
            <Stack spacing={1.5}>
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <Typography variant="h6">Binding And Autopilot</Typography>
                <Chip
                  icon={autopilot?.isRunning ? <PlayCircleFilledWhiteIcon /> : <StopCircleIcon />}
                  color={autopilot?.isRunning ? 'success' : 'default'}
                  label={autopilot?.isRunning ? 'autopilot' : 'manual'}
                />
              </Box>

              <Typography variant="body2" color="text.secondary">
                Runtime: {runtimeMode} {runtimeMode === 'unity-sim' ? `| agent: ${unityControlAgentId || 'не выбран'}` : ''}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Bound model: {binding ? `${binding.name} ${binding.version}` : 'не привязана'}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Legacy active model: {activeModel ? `${activeModel.name} ${activeModel.version}` : 'не выбрана'}
              </Typography>
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
              >
                {(selectedGroup?.versions ?? []).map((item) => (
                  <MenuItem key={item.modelId} value={item.modelId}>
                    {item.version} · {item.source}
                  </MenuItem>
                ))}
              </TextField>
              <Typography variant="body2" color="text.secondary">
                Selected install: {selectedVersion?.modelId ?? 'n/a'}
              </Typography>
              <CompatibilitySummary compatibility={selectedVersion?.compatibility ?? null} />
              <TextField
                size="small"
                label="Loop interval, ms"
                value={loopIntervalMs}
                onChange={(event) => setLoopIntervalMs(event.target.value)}
              />
              <Stack direction="row" spacing={1} flexWrap="wrap">
                <Button
                  disabled={busy || !selectedVersion || (runtimeMode === 'unity-sim' && !unityControlAgentId)}
                  variant="contained"
                  onClick={() => void handleBind()}
                >
                  Bind Version
                </Button>
                <Button
                  disabled={busy || !selectedVersion}
                  variant="outlined"
                  onClick={() => {
                    if (!selectedVersion) {
                      return
                    }

                    void onActivate(selectedVersion.modelId)
                  }}
                >
                  Set Legacy Active
                </Button>
                <Button
                  startIcon={<ModelTrainingIcon />}
                  disabled={busy || !binding || (runtimeMode === 'unity-sim' && !unityControlAgentId)}
                  variant="contained"
                  color="secondary"
                  onClick={() => void handleStart()}
                >
                  Start
                </Button>
                <Button
                  startIcon={<StopCircleIcon />}
                  disabled={busy || !autopilot?.isRunning}
                  variant="outlined"
                  color="warning"
                  onClick={() => void onStopAutopilot()}
                >
                  Stop
                </Button>
              </Stack>

              {autopilot ? (
                <Typography variant="caption" color="text.secondary">
                  Steps: {autopilot.stepsTotal} | Commands: {autopilot.commandsSent} | Last: {autopilot.lastCommand ?? 'n/a'} | throttle={autopilot.lastThrottle.toFixed(3)} steer={autopilot.lastSteer.toFixed(3)}
                </Typography>
              ) : null}
            </Stack>
          </CardContent>
        </Card>
      </Grid>

      <Grid size={{ xs: 12 }}>
        <Card>
          <CardContent>
            <Stack spacing={1.25}>
              <Typography variant="h6">Installed Models</Typography>
              {catalog.length === 0 ? <Typography color="text.secondary">Модели пока не установлены</Typography> : null}
              {catalog.map((entry) => (
                <Box
                  key={entry.name}
                  sx={{
                    border: '1px solid rgba(132, 157, 186, 0.28)',
                    borderRadius: 2,
                    px: 1.5,
                    py: 1.2,
                  }}
                >
                  <Stack spacing={1}>
                    <Typography fontWeight={700}>{entry.name}</Typography>
                    {entry.versions.map((model) => (
                      <Box
                        key={model.modelId}
                        sx={{
                          border: '1px solid rgba(132, 157, 186, 0.18)',
                          borderRadius: 2,
                          px: 1.25,
                          py: 1,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: 1,
                          flexWrap: 'wrap',
                          backgroundColor: model.modelId === selectedModelId ? 'rgba(35, 213, 171, 0.08)' : 'transparent',
                        }}
                      >
                        <Stack spacing={0.4}>
                          <Typography fontWeight={600}>{model.version}</Typography>
                          <Typography variant="caption" color="text.secondary">
                            id={model.modelId} | source={model.source}
                          </Typography>
                        </Stack>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                          {binding?.modelId === model.modelId ? <Chip size="small" color="primary" label="bound" /> : null}
                          {model.isActive ? <Chip size="small" color="success" label="legacy active" /> : null}
                          <Button
                            size="small"
                            variant={model.modelId === selectedModelId ? 'contained' : 'outlined'}
                            disabled={busy}
                            onClick={() => {
                              setSelectedName(entry.name)
                              setSelectedModelId(model.modelId)
                            }}
                          >
                            Select
                          </Button>
                        </Stack>
                      </Box>
                    ))}
                  </Stack>
                </Box>
              ))}
            </Stack>
          </CardContent>
        </Card>
      </Grid>

      {error ? (
        <Grid size={{ xs: 12 }}>
          <Alert severity="error">{error}</Alert>
        </Grid>
      ) : null}
    </Grid>
  )
}

function CompatibilitySummary({ compatibility }: { compatibility: CompatibilityHintsDto | null }) {
  const chips = [
    ...renderCompatibilityChips('runtime', compatibility?.runtimeModes),
    ...renderCompatibilityChips('vehicle', compatibility?.vehicleIds),
    ...renderCompatibilityChips('robot', compatibility?.robotKinds),
  ]

  if (chips.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        Compatibility: не указана, модель считается универсальной.
      </Typography>
    )
  }

  return (
    <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
      {chips}
    </Stack>
  )
}

function renderCompatibilityChips(prefix: string, values: string[] | undefined) {
  return (values ?? []).map((value) => (
    <Chip key={`${prefix}:${value}`} size="small" variant="outlined" label={`${prefix}: ${value}`} />
  ))
}

function parseCsv(raw: string): string[] {
  return raw
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
}
