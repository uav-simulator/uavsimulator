import PlayCircleFilledWhiteIcon from '@mui/icons-material/PlayCircleFilledWhite'
import StopCircleIcon from '@mui/icons-material/StopCircle'
import RefreshIcon from '@mui/icons-material/Refresh'
import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  LinearProgress,
  MenuItem,
  Stack,
  TextField,
  Typography,
  ToggleButton,
  ToggleButtonGroup,
} from '@mui/material'
import { useEffect, useMemo, useRef, useState } from 'react'
import {
  getDemoReplayStatus,
  listDemoReplaySessions,
  startDemoReplay,
  stopDemoReplay,
  type DemoReplayProgress,
  type DemoSessionFile,
} from '../api'

type Props = {
  clientId: string
  runtimeMode: string
  agentId?: string
}

const SPEEDS = [0.5, 1.0, 2.0, 4.0]

type ChipColor = 'default' | 'primary' | 'success' | 'warning' | 'error'
type BarColor = 'primary' | 'success' | 'warning' | 'error'

function chipColor(state: string): ChipColor {
  switch (state) {
    case 'Playing': return 'primary'
    case 'Done': return 'success'
    case 'Error': return 'error'
    case 'Stopped': return 'warning'
    default: return 'default'
  }
}

function barColor(state: string): BarColor {
  switch (state) {
    case 'Done': return 'success'
    case 'Error': return 'error'
    case 'Stopped': return 'warning'
    default: return 'primary'
  }
}

export function DemoReplayPanel({ clientId, runtimeMode, agentId }: Props) {
  const [sessions, setSessions] = useState<DemoSessionFile[]>([])
  const [selectedFile, setSelectedFile] = useState<string>('')
  const [speed, setSpeed] = useState<number>(1.0)
  const [progress, setProgress] = useState<DemoReplayProgress | null>(null)
  const [busy, setBusy] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string>('')
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const refreshSessions = async () => {
    try {
      const list = await listDemoReplaySessions()
      // Hide empty (zero-command) sessions — not useful for replay
      setSessions(list.filter(s => s.commandCount > 0))
    } catch (e) {
      setErrorMsg(`Failed to load sessions: ${e instanceof Error ? e.message : String(e)}`)
    }
  }

  // Initial load + cleanup
  useEffect(() => {
    refreshSessions()
    // Always fetch initial status to recover state after reload
    getDemoReplayStatus().then(setProgress).catch(() => {})
    return () => {
      if (pollRef.current) {
        clearInterval(pollRef.current)
        pollRef.current = null
      }
    }
  }, [])

  // Poll status while playing
  useEffect(() => {
    if (progress?.state === 'Playing' || progress?.state === 'Loading') {
      if (!pollRef.current) {
        pollRef.current = setInterval(async () => {
          try {
            const s = await getDemoReplayStatus()
            setProgress(s)
            if (s.state !== 'Playing' && s.state !== 'Loading' && pollRef.current) {
              clearInterval(pollRef.current)
              pollRef.current = null
            }
          } catch { /* network blip — keep polling */ }
        }, 250)
      }
    } else if (pollRef.current) {
      clearInterval(pollRef.current)
      pollRef.current = null
    }
  }, [progress?.state])

  const onPlay = async () => {
    if (!selectedFile) return
    setErrorMsg('')
    setBusy(true)
    try {
      await startDemoReplay({
        clientId,
        runtimeMode,
        sessionFilePath: selectedFile,
        agentId,
        speedMultiplier: speed,
      })
      const s = await getDemoReplayStatus()
      setProgress(s)
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  const onStop = async () => {
    setBusy(true)
    try {
      await stopDemoReplay()
      const s = await getDemoReplayStatus()
      setProgress(s)
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  const isPlaying = progress?.state === 'Playing' || progress?.state === 'Loading'
  const progressPct = useMemo(() => {
    if (!progress || progress.totalCommands <= 0) return 0
    return Math.min(100, Math.round((progress.currentIndex / progress.totalCommands) * 100))
  }, [progress])

  return (
    <Card>
      <CardContent>
        <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 1 }}>
          <Typography variant="h6">Demo Replay</Typography>
          <Button
            size="small"
            startIcon={<RefreshIcon />}
            onClick={refreshSessions}
            disabled={isPlaying}
          >
            Refresh
          </Button>
        </Stack>

        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Pick a recorded session, hit Play — robot re-runs your hand-driven trajectory at the chosen speed.
        </Typography>

        <Stack spacing={2}>
          <TextField
            select
            label="Session"
            size="small"
            value={selectedFile}
            onChange={(e) => setSelectedFile(e.target.value)}
            disabled={isPlaying || sessions.length === 0}
            fullWidth
          >
            {sessions.length === 0 && (
              <MenuItem value="" disabled>
                No sessions found (record one first)
              </MenuItem>
            )}
            {sessions.map((s) => (
              <MenuItem key={s.filePath} value={s.filePath}>
                {s.fileName} ({s.commandCount} cmds, {s.sizeKb} KB)
              </MenuItem>
            ))}
          </TextField>

          <Box>
            <Typography variant="caption" color="text.secondary">Speed</Typography>
            <ToggleButtonGroup
              value={speed}
              exclusive
              size="small"
              onChange={(_, v) => v !== null && setSpeed(v)}
              disabled={isPlaying}
              fullWidth
            >
              {SPEEDS.map((s) => (
                <ToggleButton key={s} value={s}>{s}x</ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>

          <Stack direction="row" spacing={1}>
            <Button
              variant="contained"
              color="primary"
              startIcon={<PlayCircleFilledWhiteIcon />}
              onClick={onPlay}
              disabled={!selectedFile || isPlaying || busy}
              fullWidth
            >
              Play
            </Button>
            <Button
              variant="outlined"
              color="warning"
              startIcon={<StopCircleIcon />}
              onClick={onStop}
              disabled={!isPlaying || busy}
              fullWidth
            >
              Stop
            </Button>
          </Stack>

          {progress && (
            <Box>
              <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 0.5 }}>
                <Chip
                  label={progress.state}
                  size="small"
                  color={chipColor(progress.state)}
                />
                <Typography variant="caption" color="text.secondary">
                  {progress.currentIndex}/{progress.totalCommands} cmds
                  {progress.lastCommand ? ` • last: ${progress.lastCommand}` : ''}
                  {progress.elapsedMs > 0 ? ` • ${(progress.elapsedMs / 1000).toFixed(1)}s` : ''}
                </Typography>
              </Stack>
              <LinearProgress
                variant="determinate"
                value={progressPct}
                color={barColor(progress.state)}
              />
              {progress.lastError && (
                <Typography variant="caption" color="error" sx={{ mt: 0.5, display: 'block' }}>
                  {progress.lastError}
                </Typography>
              )}
            </Box>
          )}

          {errorMsg && (
            <Typography variant="caption" color="error">{errorMsg}</Typography>
          )}
        </Stack>
      </CardContent>
    </Card>
  )
}
