import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import PlayArrowIcon from '@mui/icons-material/PlayArrow'
import RefreshIcon from '@mui/icons-material/Refresh'
import { generateMazeScenario, type MazeGenerateResult } from '../api'

type Props = {
  clientId: string
  runtimeMode: string
  unityControlAgentId?: string
  unityVehicleId?: string
  onGenerated?: (result: MazeGenerateResult) => void | Promise<void>
}

function parseIntField(value: string, fallback: number) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseFloatField(value: string, fallback: number) {
  const parsed = Number.parseFloat(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : fallback
}

export function MazeGeneratorPanel({
  clientId,
  runtimeMode,
  unityControlAgentId,
  unityVehicleId,
  onGenerated,
}: Props) {
  const [mazeResult, setMazeResult] = useState<MazeGenerateResult | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [mazeSeed, setMazeSeed] = useState('42')
  const [mazeLengthCells, setMazeLengthCells] = useState('12')
  const [mazeLeftTurns, setMazeLeftTurns] = useState('2')
  const [mazeRightTurns, setMazeRightTurns] = useState('1')
  const [mazeCorridorWidth, setMazeCorridorWidth] = useState('0.45')
  const [mazeWallHeight, setMazeWallHeight] = useState('0.25')
  const [mazeVehicleId, setMazeVehicleId] = useState(unityVehicleId || 'vehicle.ks0223.v1')
  const [mazeAgentId, setMazeAgentId] = useState(unityControlAgentId || 'agent-1')
  const [generatingMaze, setGeneratingMaze] = useState(false)

  useEffect(() => {
    if (unityVehicleId && mazeVehicleId === 'vehicle.ks0223.v1') {
      setMazeVehicleId(unityVehicleId)
    }
  }, [mazeVehicleId, unityVehicleId])

  useEffect(() => {
    if (unityControlAgentId && mazeAgentId === 'agent-1') {
      setMazeAgentId(unityControlAgentId)
    }
  }, [mazeAgentId, unityControlAgentId])

  const randomizeMazeSeed = () => {
    setMazeSeed(String(Math.floor(Math.random() * 1_000_000)))
  }

  const onGenerateMaze = async () => {
    setGeneratingMaze(true)
    setErrorMsg(null)
    setMazeResult(null)
    try {
      const result = await generateMazeScenario({
        seed: parseIntField(mazeSeed, 42),
        lengthCells: parseIntField(mazeLengthCells, 12),
        corridorWidthM: parseFloatField(mazeCorridorWidth, 0.45),
        leftTurns: parseIntField(mazeLeftTurns, 2),
        rightTurns: parseIntField(mazeRightTurns, 1),
        wallHeightM: parseFloatField(mazeWallHeight, 0.25),
        vehicleId: mazeVehicleId.trim() || undefined,
        agentId: mazeAgentId.trim() || undefined,
        cameraProfile: 'high',
        clientId,
        runtimeMode,
      })
      setMazeResult(result)
      await onGenerated?.(result)
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : String(e))
    } finally {
      setGeneratingMaze(false)
    }
  }

  return (
    <Stack spacing={1.5}>
      <Box>
        <Typography variant="subtitle2">Maze generator</Typography>
        <Typography variant="caption" color="text.secondary">
          Генерирует новую Maze трассу в текущем Unity runtime без ручного редактирования YAML.
        </Typography>
      </Box>

      {errorMsg && (
        <Alert severity="error" onClose={() => setErrorMsg(null)}>
          {errorMsg}
        </Alert>
      )}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
        <TextField
          size="small"
          label="Seed"
          value={mazeSeed}
          onChange={e => setMazeSeed(e.target.value)}
          sx={{ flex: 1 }}
        />
        <Button
          size="small"
          variant="outlined"
          startIcon={<RefreshIcon />}
          onClick={randomizeMazeSeed}
          sx={{ minWidth: 128 }}
        >
          Random
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
        <TextField
          size="small"
          label="Length cells"
          value={mazeLengthCells}
          onChange={e => setMazeLengthCells(e.target.value)}
          sx={{ flex: 1 }}
        />
        <TextField
          size="small"
          label="Left turns"
          value={mazeLeftTurns}
          onChange={e => setMazeLeftTurns(e.target.value)}
          sx={{ flex: 1 }}
        />
        <TextField
          size="small"
          label="Right turns"
          value={mazeRightTurns}
          onChange={e => setMazeRightTurns(e.target.value)}
          sx={{ flex: 1 }}
        />
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
        <TextField
          size="small"
          label="Corridor width, m"
          value={mazeCorridorWidth}
          onChange={e => setMazeCorridorWidth(e.target.value)}
          sx={{ flex: 1 }}
        />
        <TextField
          size="small"
          label="Wall height, m"
          value={mazeWallHeight}
          onChange={e => setMazeWallHeight(e.target.value)}
          sx={{ flex: 1 }}
        />
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
        <TextField
          size="small"
          label="Vehicle"
          value={mazeVehicleId}
          onChange={e => setMazeVehicleId(e.target.value)}
          sx={{ flex: 1.4 }}
        />
        <TextField
          size="small"
          label="Agent"
          value={mazeAgentId}
          onChange={e => setMazeAgentId(e.target.value)}
          sx={{ flex: 0.8 }}
        />
      </Stack>

      <Button
        variant="contained"
        startIcon={generatingMaze ? <CircularProgress size={16} color="inherit" /> : <PlayArrowIcon />}
        onClick={onGenerateMaze}
        disabled={generatingMaze}
      >
        {generatingMaze ? 'Generating…' : 'Generate maze'}
      </Button>

      {mazeResult && (
        <Box>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {mazeResult.maze?.seed !== undefined && (
              <Chip size="small" color="success" variant="outlined" label={`seed: ${mazeResult.maze.seed}`} />
            )}
            {mazeResult.maze?.lengthCells && (
              <Chip size="small" variant="outlined" label={`length: ${mazeResult.maze.lengthCells}`} />
            )}
            {mazeResult.maze?.leftTurns && mazeResult.maze?.rightTurns && (
              <Chip size="small" variant="outlined" label={`turns L/R: ${mazeResult.maze.leftTurns}/${mazeResult.maze.rightTurns}`} />
            )}
            {mazeResult.maze?.trackId && (
              <Chip size="small" variant="outlined" label={`track: ${mazeResult.maze.trackId}`} />
            )}
            {mazeResult.reset?.hasFrame !== undefined && (
              <Chip size="small" variant="outlined" label={`frame: ${mazeResult.reset.hasFrame ? 'yes' : 'no'}`} />
            )}
          </Stack>
          {mazeResult.rawOutput && (
            <Typography variant="caption" sx={{ display: 'block', mt: 1, whiteSpace: 'pre-wrap' }}>
              {mazeResult.rawOutput}
            </Typography>
          )}
        </Box>
      )}
    </Stack>
  )
}
