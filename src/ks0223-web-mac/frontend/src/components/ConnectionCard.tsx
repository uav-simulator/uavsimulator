import BoltIcon from '@mui/icons-material/Bolt'
import LinkOffIcon from '@mui/icons-material/LinkOff'
import PortableWifiOffIcon from '@mui/icons-material/PortableWifiOff'
import SensorsIcon from '@mui/icons-material/Sensors'
import TuneIcon from '@mui/icons-material/Tune'
import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { discoverUnityRuntimes } from '../api'
import type { StatusDto, UnityRuntimeCatalogDto } from '../types'

type Props = {
  status: StatusDto | null
  busy: boolean
  runtimeMode: string
  onRuntimeModeChange: (value: string) => void
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
}

function formatLatency(value: number | null): string {
  if (value === null) {
    return 'н/д'
  }

  return `${Math.round(value)} ms`
}

export function ConnectionCard({
  status,
  busy,
  runtimeMode,
  onRuntimeModeChange,
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
}: Props) {
  const [unityDialogOpen, setUnityDialogOpen] = useState(false)
  const [unityDialogBusy, setUnityDialogBusy] = useState(false)
  const [unityDialogError, setUnityDialogError] = useState<string | null>(null)
  const [trackDraft, setTrackDraft] = useState('')
  const [agentDrafts, setAgentDrafts] = useState<Array<{ agentId: string; vehicleId: string; isPrimary: boolean }>>([])
  const [collisionsDraft, setCollisionsDraft] = useState(unityCollisionsEnabled)
  const [seeEachOtherDraft, setSeeEachOtherDraft] = useState(unitySeeEachOther)
  const [discoveredPorts, setDiscoveredPorts] = useState<Array<{ port: number; baseUrl: string }>>([])
  const [discovering, setDiscovering] = useState(false)

  const handleDiscover = useCallback(async () => {
    setDiscovering(true)
    try {
      const host = targetHost.trim() || (runtimeMode === 'unity-sim' ? '127.0.0.1' : '192.168.1.121')
      const basePort = Number(targetPort) || (runtimeMode === 'unity-sim' ? 8000 : 5051)
      const result = await discoverUnityRuntimes(host, basePort, basePort + 7)
      setDiscoveredPorts(result.instances.map((i) => ({ port: i.port, baseUrl: i.baseUrl })))
    } catch {
      setDiscoveredPorts([])
    } finally {
      setDiscovering(false)
    }
  }, [targetHost, targetPort, runtimeMode])

  const tcpConnected = status?.tcpConnected ?? false
  const isUnityMode = runtimeMode === 'unity-sim'
  const connectionLabel = isUnityMode ? 'Unity API подключен' : 'TCP подключен'
  const disconnectedLabel = isUnityMode ? 'Unity API отключен' : 'TCP отключен'
  const hostLabel = isUnityMode ? 'IP или host Unity runtime' : 'IP или host Raspberry Pi'
  const defaultPort = isUnityMode ? 8000 : 5051
  const runtimeLabel = status?.runtimeLabel ?? (isUnityMode ? 'Unity Simulator Vehicle' : 'Keyestudio KS0223 (Real Robot)')
  // Wrapped in useMemo so identity is stable across renders when the
  // upstream `unityCatalog` is unchanged — without this, the downstream
  // useMemos (selectedVehicleTitle / selectedControlAgentTitle /
  // selectedCameraAgentTitle / effectiveAgentOptions) would recompute on
  // every render because their `vehicles`/`agents` deps would be new arrays.
  const tracks = useMemo(() => unityCatalog?.tracks ?? [], [unityCatalog])
  const vehicles = useMemo(() => unityCatalog?.vehicles ?? [], [unityCatalog])
  const agents = useMemo(() => unityCatalog?.agents ?? [], [unityCatalog])

  const hasUnityOptions = tracks.length > 0 && vehicles.length > 0
  const dialogBusy = unityDialogBusy || unityCatalogBusy
  const connectApplyImmediately = tcpConnected && isUnityMode

  const selectedTrackTitle = useMemo(() => {
    if (!unityCatalog || !unityCatalog.selectedTrackId) {
      return 'не выбран'
    }

    return unityCatalog.tracks.find((item) => item.id === unityCatalog.selectedTrackId)?.displayName ?? unityCatalog.selectedTrackId
  }, [unityCatalog])

  const selectedVehicleTitle = useMemo(() => {
    if (!unityCatalog) {
      return 'не выбрана'
    }

    const primary = unityCatalog.agents.find((agent) => agent.isPrimary) ?? unityCatalog.agents[0]
    const vehicleId = primary?.vehicleId || unityCatalog.selectedVehicleId
    if (!vehicleId) {
      return 'не выбрана'
    }

    return unityCatalog.vehicles.find((item) => item.id === vehicleId)?.displayName ?? vehicleId
  }, [unityCatalog])

  const selectedCameraModeTitle = useMemo(() => {
    switch (unityCameraMode) {
      case 'bumper':
        return 'Bumper'
      case 'chase':
        return 'Chase'
      case 'spectator':
        return 'Spectator'
      case 'top_down':
        return 'Top Down'
      default:
        return 'Driver'
    }
  }, [unityCameraMode])

  const selectedControlAgentTitle = useMemo(() => {
    if (!unityControlAgentId) {
      return 'не выбран'
    }

    const agent = agents.find((item) => item.agentId === unityControlAgentId)
    return agent ? `${agent.agentId} · ${agent.displayName}` : unityControlAgentId
  }, [agents, unityControlAgentId])

  const selectedCameraAgentTitle = useMemo(() => {
    if (!unityCameraAgentId) {
      return 'не выбран'
    }

    const agent = agents.find((item) => item.agentId === unityCameraAgentId)
    return agent ? `${agent.agentId} · ${agent.displayName}` : unityCameraAgentId
  }, [agents, unityCameraAgentId])

  useEffect(() => {
    if (!unityDialogOpen) {
      return
    }

    if (unityCatalog?.selectedTrackId) {
      setTrackDraft(unityCatalog.selectedTrackId)
    }

    setCollisionsDraft(unityCollisionsEnabled)
    setSeeEachOtherDraft(unitySeeEachOther)

    setAgentDrafts(
      (unityCatalog?.agents ?? []).map((agent) => ({
        agentId: agent.agentId,
        vehicleId: agent.vehicleId,
        isPrimary: agent.isPrimary,
      })),
    )
    // unityCollisionsEnabled / unitySeeEachOther are intentionally read
    // only when the dialog opens (they reset the draft once); excluding
    // them from deps prevents the dialog from snapping back to current
    // values mid-edit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unityCatalog, unityDialogOpen])

  const effectiveAgentOptions = useMemo(() => {
    return agents.map((agent) => {
      const vehicleId = agent.vehicleId
      const displayName =
        vehicles.find((vehicle) => vehicle.id === vehicleId)?.displayName ??
        unityCatalog?.agents.find((item) => item.agentId === agent.agentId)?.displayName ??
        vehicleId
      return { agentId: agent.agentId, vehicleId, displayName }
    })
  }, [agents, unityCatalog?.agents, vehicles])

  const handleOpenUnityDialog = async () => {
    setUnityDialogOpen(true)
    setUnityDialogError(null)
    setUnityDialogBusy(true)
    try {
      await onUnityCatalogRefresh()
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      setUnityDialogError(message)
    } finally {
      setUnityDialogBusy(false)
    }
  }

  const handleSaveUnitySelection = async () => {
    const firstVehicle = agentDrafts[0]?.vehicleId || unityCatalog?.selectedVehicleId || vehicles[0]?.id || ''
    if (!trackDraft || !firstVehicle) {
      setUnityDialogError('Выбери трек и добавь хотя бы одну машинку')
      return
    }

    setUnityDialogBusy(true)
    setUnityDialogError(null)
    try {
      await onUnitySelectionSave(
        trackDraft,
        firstVehicle,
        agentDrafts.map((agent) => ({
          agentId: agent.agentId,
          vehicleId: agent.vehicleId,
          isPrimary: false,
        })),
        connectApplyImmediately,
        collisionsDraft,
        seeEachOtherDraft,
      )
      setUnityDialogOpen(false)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      setUnityDialogError(message)
    } finally {
      setUnityDialogBusy(false)
    }
  }

  return (
    <Card sx={{ minHeight: 260 }}>
      <CardContent>
        <Stack spacing={2}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="h6">Подключение</Typography>
            {busy ? <CircularProgress size={20} /> : null}
          </Box>

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Chip
              icon={tcpConnected ? <SensorsIcon /> : <PortableWifiOffIcon />}
              color={tcpConnected ? 'success' : 'default'}
              label={tcpConnected ? connectionLabel : disconnectedLabel}
            />
            <Chip label={runtimeLabel} variant="outlined" />
            <Chip label={`UI-клиенты: ${status?.uiConnectedClients ?? 0}`} variant="outlined" />
            <Chip label={`Задержка: ${formatLatency(status?.latencyMs ?? null)}`} variant="outlined" />
          </Stack>

          <Typography variant="body2" color={status?.lastError ? 'error.main' : 'text.secondary'}>
            Последняя ошибка: {status?.lastError ?? 'нет'}
          </Typography>

          <Typography variant="caption" color="text.secondary">
            Configured endpoint: {targetHost || '—'}:{targetPort || String(defaultPort)}
          </Typography>
          <Typography variant="caption" color={tcpConnected ? 'success.light' : 'text.secondary'}>
            Connected endpoint: {status?.targetHost ?? '—'}:{status?.targetPort ?? '—'}
          </Typography>

          <TextField
            size="small"
            select
            label="Режим runtime"
            value={runtimeMode}
            onChange={(event) => onRuntimeModeChange(event.target.value)}
            disabled={busy || tcpConnected}
          >
            <MenuItem value="real-robot">Real robot</MenuItem>
            <MenuItem value="unity-sim">Unity simulator</MenuItem>
          </TextField>

          <TextField
            size="small"
            label={hostLabel}
            value={targetHost}
            onChange={(event) => onTargetHostChange(event.target.value)}
            disabled={busy}
            placeholder={isUnityMode ? '127.0.0.1' : '192.168.1.121'}
            helperText="Host или IP целевого runtime"
          />

          <TextField
            size="small"
            label="Порт runtime"
            type="number"
            value={targetPort}
            onChange={(event) => onTargetPortChange(event.target.value)}
            disabled={busy}
            inputProps={{ min: 1, max: 65535, step: 1 }}
            helperText={`Порт по умолчанию: ${defaultPort}`}
          />

          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button variant="text" color="secondary" onClick={onResetEndpoint} disabled={busy}>
              Сбросить
            </Button>
            {isUnityMode ? (
              <Button
                variant="outlined"
                size="small"
                onClick={() => void handleDiscover()}
                disabled={discovering}
                startIcon={discovering ? <CircularProgress size={14} /> : <SensorsIcon />}
              >
                {discovering ? 'Сканирую...' : 'Найти рантаймы'}
              </Button>
            ) : null}
          </Stack>

          {discoveredPorts.length > 0 ? (
            <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
              <Typography variant="caption" color="text.secondary" sx={{ mr: 0.5, alignSelf: 'center' }}>
                Найдено:
              </Typography>
              {discoveredPorts.map((inst) => (
                <Chip
                  key={inst.port}
                  label={`:${inst.port}`}
                  size="small"
                  color={String(inst.port) === targetPort ? 'primary' : 'default'}
                  variant={String(inst.port) === targetPort ? 'filled' : 'outlined'}
                  clickable
                  onClick={() => onTargetPortChange(String(inst.port))}
                />
              ))}
            </Stack>
          ) : null}

          {isUnityMode ? (
            <Stack spacing={1}>
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                <Chip label={`Трек: ${selectedTrackTitle}`} size="small" />
                <Chip label={`Машинка: ${selectedVehicleTitle}`} size="small" />
                <Chip label={`Камера: ${selectedCameraModeTitle}`} size="small" />
                <Chip label={`Camera agent: ${selectedCameraAgentTitle}`} size="small" />
                <Chip label={`Control agent: ${selectedControlAgentTitle}`} size="small" />
              </Stack>
              {tcpConnected && agents.length === 0 ? (
                <Typography variant="body2" color="warning.main">
                  В симуляции пока нет машинок. Открой popup и добавь agent.
                </Typography>
              ) : null}
              <Button
                variant="outlined"
                color="secondary"
                startIcon={<TuneIcon />}
                onClick={() => void handleOpenUnityDialog()}
                disabled={busy}
              >
                Выбрать сцену и машинку
              </Button>
            </Stack>
          ) : null}

          <Stack direction="row" spacing={1}>
            <Button
              variant="contained"
              startIcon={<BoltIcon />}
              onClick={() => void onConnect()}
              disabled={busy || tcpConnected}
            >
              Подключить
            </Button>
            <Button
              variant="outlined"
              color="warning"
              startIcon={<LinkOffIcon />}
              onClick={() => void onDisconnect()}
              disabled={busy || !tcpConnected}
            >
              Отключить
            </Button>
          </Stack>
        </Stack>
      </CardContent>

      <Dialog open={unityDialogOpen && isUnityMode} onClose={() => setUnityDialogOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Настройки Unity симулятора</DialogTitle>
        <DialogContent sx={{ pt: '8px !important' }}>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              Выбранный трек и список машинок будут применяться при следующем подключении. Если соединение уже активно, настройки
              применяются сразу через reset.
            </Typography>

            <TextField
              select
              size="small"
              label="Трек"
              value={trackDraft}
              onChange={(event) => setTrackDraft(event.target.value)}
              disabled={dialogBusy || tracks.length === 0}
            >
              {tracks.map((track) => (
                <MenuItem key={track.id} value={track.id}>
                  {track.displayName}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              size="small"
              label="Camera mode"
              value={unityCameraMode}
              onChange={(event) => onUnityCameraModeChange(event.target.value)}
              disabled={dialogBusy}
            >
              <MenuItem value="driver">Driver</MenuItem>
              <MenuItem value="bumper">Bumper</MenuItem>
              <MenuItem value="chase">Chase</MenuItem>
              <MenuItem value="spectator">Spectator</MenuItem>
              <MenuItem value="top_down">Top Down 🔭</MenuItem>
            </TextField>

            <Stack direction="row" spacing={2} flexWrap="wrap">
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={collisionsDraft}
                    onChange={(e) => setCollisionsDraft(e.target.checked)}
                    disabled={dialogBusy}
                  />
                }
                label="Коллизии между машинками"
              />
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={seeEachOtherDraft}
                    onChange={(e) => setSeeEachOtherDraft(e.target.checked)}
                    disabled={dialogBusy}
                  />
                }
                label="Видят друг друга"
              />
            </Stack>

            <Stack spacing={1}>
              <Typography variant="subtitle2">Машинки на трассе</Typography>
              {agentDrafts.map((agent, index) => (
                <Stack key={`${agent.agentId}-${index}`} direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                  <TextField
                    size="small"
                    label="Agent id"
                    value={agent.agentId}
                    onChange={(event) => {
                      const next = [...agentDrafts]
                      next[index] = { ...next[index], agentId: event.target.value.trim() || `agent-${index + 1}` }
                      setAgentDrafts(next)
                    }}
                    disabled={dialogBusy}
                  />
                  <TextField
                    select
                    size="small"
                    label="Машинка"
                    value={agent.vehicleId}
                    onChange={(event) => {
                      const next = [...agentDrafts]
                      next[index] = { ...next[index], vehicleId: event.target.value }
                      setAgentDrafts(next)
                    }}
                    disabled={dialogBusy || vehicles.length === 0}
                    sx={{ minWidth: { sm: 260 } }}
                  >
                    {vehicles.map((vehicle) => (
                      <MenuItem key={vehicle.id} value={vehicle.id}>
                        {vehicle.displayName}
                      </MenuItem>
                    ))}
                  </TextField>
                  <Button
                    variant="outlined"
                    color="warning"
                    onClick={() => {
                      const next = agentDrafts.filter((_, currentIndex) => currentIndex !== index)
                      setAgentDrafts(next)
                      if (unityControlAgentId === agent.agentId) {
                        onUnityControlAgentIdChange('')
                      }
                      if (unityCameraAgentId === agent.agentId) {
                        onUnityCameraAgentIdChange('')
                      }
                    }}
                    disabled={dialogBusy}
                  >
                    Удалить
                  </Button>
                </Stack>
              ))}
              <Button
                variant="outlined"
                onClick={() => {
                  const usedIds = new Set(agentDrafts.map((item) => item.agentId.trim()).filter(Boolean))
                  let nextIndex = agentDrafts.length + 1
                  let candidate = `agent-${nextIndex}`
                  while (usedIds.has(candidate)) {
                    nextIndex += 1
                    candidate = `agent-${nextIndex}`
                  }

                  const fallbackVehicleId = agentDrafts[0]?.vehicleId || vehicles[0]?.id || ''
                  if (!fallbackVehicleId) {
                    return
                  }

                  const next = [...agentDrafts, { agentId: candidate, vehicleId: fallbackVehicleId, isPrimary: false }]
                  setAgentDrafts(next)
                }}
                disabled={dialogBusy || vehicles.length === 0}
              >
                Добавить машинку
              </Button>
            </Stack>

            <TextField
              select
              size="small"
              label="Управляемый agent"
              value={unityControlAgentId}
              onChange={(event) => onUnityControlAgentIdChange(event.target.value)}
              disabled={dialogBusy || effectiveAgentOptions.length === 0}
              helperText="Команды этой вкладки будут идти через выбранный agent"
            >
              <MenuItem value="">{effectiveAgentOptions.length === 0 ? 'Нет машинок' : 'Не выбрано'}</MenuItem>
              {effectiveAgentOptions.map((agent) => (
                <MenuItem key={agent.agentId} value={agent.agentId}>
                  {agent.agentId} · {agent.displayName}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              size="small"
              label="Camera agent"
              value={unityCameraAgentId}
              onChange={(event) => {
                onUnityCameraAgentIdChange(event.target.value)
                onUnityControlAgentIdChange(event.target.value)
              }}
              disabled={dialogBusy || effectiveAgentOptions.length === 0}
              helperText="Камера и управление синхронизируются на один agent"
            >
              <MenuItem value="">{effectiveAgentOptions.length === 0 ? 'Нет машинок' : 'Не выбрано'}</MenuItem>
              {effectiveAgentOptions.map((agent) => (
                <MenuItem key={agent.agentId} value={agent.agentId}>
                  {agent.agentId} · {agent.displayName}
                </MenuItem>
              ))}
            </TextField>

            <Typography variant="body2" color="text.secondary">
              Машинок в конфигурации: {agentDrafts.length}
            </Typography>

            {!hasUnityOptions ? (
              <Typography variant="body2" color="warning.main">
                Каталог симулятора пустой. Проверь host/port Unity runtime и нажми обновить.
              </Typography>
            ) : null}

            {unityDialogError ? (
              <Typography variant="body2" color="error.main">
                Ошибка: {unityDialogError}
              </Typography>
            ) : null}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setUnityDialogOpen(false)} disabled={dialogBusy}>
            Закрыть
          </Button>
          <Button onClick={() => void handleOpenUnityDialog()} disabled={dialogBusy}>
            Обновить
          </Button>
          <Button
            variant="contained"
            onClick={() => void handleSaveUnitySelection()}
            disabled={dialogBusy || !trackDraft}
          >
            Применить
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  )
}
