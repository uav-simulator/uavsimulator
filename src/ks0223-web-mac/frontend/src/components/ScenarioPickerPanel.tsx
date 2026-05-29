import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Divider,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import PlayArrowIcon from '@mui/icons-material/PlayArrow'
import RefreshIcon from '@mui/icons-material/Refresh'
import {
  listScenarios,
  loadScenario,
  type ScenarioFile,
  type ScenarioLoadResult,
} from '../api'
import { MazeGeneratorPanel } from './MazeGeneratorPanel'

type Props = {
  clientId: string
  runtimeMode: string
  unityControlAgentId?: string
  unityVehicleId?: string
}

/**
 * ScenarioPickerPanel — choose a scenario YAML and load it into the running
 * Unity simulator. Backend shells out to `rusim scenario print-reset <file>`,
 * then POSTs the payload to Unity's /reset endpoint on port 8000.
 *
 * Used to switch between e.g. corridor-sim2real, swarm visualization, and
 * the POLYGON city demo without touching the terminal.
 */
export function ScenarioPickerPanel({
  clientId,
  runtimeMode,
  unityControlAgentId,
  unityVehicleId,
}: Props) {
  const [scenarios, setScenarios] = useState<ScenarioFile[]>([])
  const [scenariosDir, setScenariosDir] = useState<string>('')
  const [warning, setWarning] = useState<string | null>(null)
  const [selectedPath, setSelectedPath] = useState<string>('')
  const [refreshing, setRefreshing] = useState(false)
  const [loading, setLoading] = useState(false)
  const [lastResult, setLastResult] = useState<ScenarioLoadResult | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const refreshScenarios = async () => {
    setRefreshing(true)
    setErrorMsg(null)
    try {
      const resp = await listScenarios()
      setScenarios(resp.items ?? [])
      setScenariosDir(resp.scenariosDir ?? '')
      setWarning(resp.warning ?? null)
      // Preselect demo-city-polygon if present, otherwise first item.
      if (resp.items && resp.items.length > 0 && !selectedPath) {
        const cityDemo = resp.items.find(s => s.displayName === 'demo-city-polygon')
        setSelectedPath(cityDemo?.filePath ?? resp.items[0].filePath)
      }
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }

  useEffect(() => {
    void refreshScenarios()
    // Mount-only: we want the initial scenario list once. `refreshScenarios`
    // is recreated on every render but its closure is stable in intent
    // (just calls listScenarios + setState). Re-running on its identity
    // would refresh on every render — clearly not what we want.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const onLoad = async () => {
    if (!selectedPath) return
    setLoading(true)
    setErrorMsg(null)
    setLastResult(null)
    try {
      const result = await loadScenario(selectedPath)
      setLastResult(result)
    } catch (e) {
      setErrorMsg(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }

  const selectedScenario = scenarios.find(s => s.filePath === selectedPath)

  return (
    <Card sx={{ minWidth: 320 }}>
      <CardContent>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
          <Typography variant="h6">Scenario</Typography>
          <Button
            size="small"
            startIcon={refreshing ? <CircularProgress size={16} /> : <RefreshIcon />}
            onClick={refreshScenarios}
            disabled={refreshing}
          >
            Refresh
          </Button>
        </Stack>

        {warning && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            {warning}
          </Alert>
        )}

        {errorMsg && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setErrorMsg(null)}>
            {errorMsg}
          </Alert>
        )}

        <TextField
          select
          fullWidth
          size="small"
          label="Scenario file"
          value={selectedPath}
          onChange={e => setSelectedPath(e.target.value)}
          disabled={scenarios.length === 0}
          sx={{ mb: 2 }}
        >
          {scenarios.length === 0 && (
            <MenuItem value="" disabled>
              No scenarios found
            </MenuItem>
          )}
          {scenarios.map(s => (
            <MenuItem key={s.filePath} value={s.filePath}>
              {s.displayName}
            </MenuItem>
          ))}
        </TextField>

        {selectedScenario && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
            {selectedScenario.fileName} · {(selectedScenario.sizeBytes / 1024).toFixed(1)} KB
          </Typography>
        )}

        <Button
          variant="contained"
          fullWidth
          startIcon={loading ? <CircularProgress size={16} color="inherit" /> : <PlayArrowIcon />}
          onClick={onLoad}
          disabled={!selectedPath || loading}
        >
          {loading ? 'Loading…' : 'Load scenario'}
        </Button>

        {lastResult && (
          <Box mt={2}>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {lastResult.scenarioId && (
                <Chip size="small" label={`scenario: ${lastResult.scenarioId}`} color="success" variant="outlined" />
              )}
              {lastResult.selectedTrackId && (
                <Chip size="small" label={`track: ${lastResult.selectedTrackId}`} variant="outlined" />
              )}
              {lastResult.selectedVehicleId && (
                <Chip size="small" label={`vehicle: ${lastResult.selectedVehicleId}`} variant="outlined" />
              )}
              {typeof lastResult.agentsConfigured === 'number' && (
                <Chip size="small" label={`agents: ${lastResult.agentsConfigured}`} variant="outlined" />
              )}
            </Stack>
            {lastResult.rawOutput && (
              <Typography variant="caption" sx={{ display: 'block', mt: 1, whiteSpace: 'pre-wrap' }}>
                {lastResult.rawOutput}
              </Typography>
            )}
          </Box>
        )}

        <Divider sx={{ my: 2.5 }} />

        <MazeGeneratorPanel
          clientId={clientId}
          runtimeMode={runtimeMode}
          unityControlAgentId={unityControlAgentId}
          unityVehicleId={unityVehicleId}
          onGenerated={() => setLastResult(null)}
        />

        {scenariosDir && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
            {scenariosDir}
          </Typography>
        )}
      </CardContent>
    </Card>
  )
}
