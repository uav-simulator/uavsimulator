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
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMemo, useState } from 'react'
import type { AutopilotStatusDto, ModelInfoDto } from '../types'

type Props = {
  models: ModelInfoDto[]
  activeModel: ModelInfoDto | null
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
  onStartAutopilot: (payload: { modelId?: string; agentId?: string; loopIntervalMs?: number }) => Promise<void>
  onStopAutopilot: () => Promise<void>
}

export function ModelControlPage({
  models,
  activeModel,
  autopilot,
  runtimeMode,
  unityControlAgentId,
  busy,
  onRefresh,
  onUpload,
  onActivate,
  onStartAutopilot,
  onStopAutopilot,
}: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [name, setName] = useState('')
  const [version, setVersion] = useState('')
  const [source, setSource] = useState('python-rl-api')
  const [loopIntervalMs, setLoopIntervalMs] = useState('140')
  const [error, setError] = useState<string | null>(null)

  const effectiveActiveModel = useMemo(
    () => activeModel ?? models.find((item) => item.isActive) ?? null,
    [activeModel, models],
  )

  const handleUpload = async () => {
    if (!file) {
      setError('Выбери `.onnx` файл модели')
      return
    }

    setError(null)
    try {
      await onUpload({
        file,
        name: name.trim() || undefined,
        version: version.trim() || undefined,
        source: source.trim() || undefined,
        metadata: JSON.stringify({ policy: 'telemetry-throttle-steer-v1' }),
        metrics: JSON.stringify({ note: 'baseline upload from web-ui' }),
      })
      setFile(null)
      setName('')
      setVersion('')
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleStart = async () => {
    setError(null)
    try {
      const parsedLoop = Number(loopIntervalMs)
      await onStartAutopilot({
        modelId: effectiveActiveModel?.modelId,
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
                ONNX-артефакт + регистрация в backend model registry.
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
              <Stack direction="row" spacing={1}>
                <Button disabled={busy} onClick={() => void onRefresh()} variant="outlined">
                  Refresh
                </Button>
                <Button disabled={busy || !file} onClick={() => void handleUpload()} variant="contained">
                  Upload
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
                <Typography variant="h6">Autopilot</Typography>
                <Chip
                  icon={autopilot?.isRunning ? <PlayCircleFilledWhiteIcon /> : <StopCircleIcon />}
                  color={autopilot?.isRunning ? 'success' : 'default'}
                  label={autopilot?.isRunning ? 'autopilot' : 'manual'}
                />
              </Box>

              <Typography variant="body2" color="text.secondary">
                Active model: {effectiveActiveModel?.name ?? 'не выбрана'} ({effectiveActiveModel?.modelId ?? 'n/a'})
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Runtime: {runtimeMode} {runtimeMode === 'unity-sim' ? `| agent: ${unityControlAgentId || 'не выбран'}` : ''}
              </Typography>
              <TextField
                size="small"
                label="Loop interval, ms"
                value={loopIntervalMs}
                onChange={(event) => setLoopIntervalMs(event.target.value)}
              />
              <Stack direction="row" spacing={1}>
                <Button
                  startIcon={<ModelTrainingIcon />}
                  disabled={busy || !effectiveActiveModel || (runtimeMode === 'unity-sim' && !unityControlAgentId)}
                  variant="contained"
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
              <Typography variant="h6">Model Registry</Typography>
              {models.length === 0 ? <Typography color="text.secondary">Модели пока не загружены</Typography> : null}
              {models.map((model) => (
                <Box
                  key={model.modelId}
                  sx={{
                    border: '1px solid rgba(132, 157, 186, 0.28)',
                    borderRadius: 2,
                    px: 1.5,
                    py: 1.2,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: 1,
                    flexWrap: 'wrap',
                  }}
                >
                  <Stack spacing={0.4}>
                    <Typography fontWeight={700}>{model.name}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      id={model.modelId} | version={model.version} | source={model.source}
                    </Typography>
                  </Stack>
                  <Stack direction="row" spacing={1} alignItems="center">
                    {model.isActive ? <Chip size="small" color="success" label="active" /> : null}
                    <Button
                      size="small"
                      variant={model.isActive ? 'outlined' : 'contained'}
                      disabled={busy || model.isActive}
                      onClick={() => void onActivate(model.modelId)}
                    >
                      Activate
                    </Button>
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
