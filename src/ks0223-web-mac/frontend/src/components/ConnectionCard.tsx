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
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useMemo, useState } from 'react'
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
  onConnect: () => Promise<void>
  onDisconnect: () => Promise<void>
  unityCatalog: UnityRuntimeCatalogDto | null
  unityCatalogBusy: boolean
  onUnityCatalogRefresh: () => Promise<void>
  onUnitySelectionSave: (trackId: string, vehicleId: string, applyImmediately: boolean) => Promise<void>
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
  onConnect,
  onDisconnect,
  unityCatalog,
  unityCatalogBusy,
  onUnityCatalogRefresh,
  onUnitySelectionSave,
}: Props) {
  const [unityDialogOpen, setUnityDialogOpen] = useState(false)
  const [unityDialogBusy, setUnityDialogBusy] = useState(false)
  const [unityDialogError, setUnityDialogError] = useState<string | null>(null)
  const [trackDraft, setTrackDraft] = useState('')
  const [vehicleDraft, setVehicleDraft] = useState('')

  const tcpConnected = status?.tcpConnected ?? false
  const isUnityMode = runtimeMode === 'unity-sim'
  const connectionLabel = isUnityMode ? 'Unity API подключен' : 'TCP подключен'
  const disconnectedLabel = isUnityMode ? 'Unity API отключен' : 'TCP отключен'
  const hostLabel = isUnityMode ? 'IP или host Unity runtime' : 'IP или host Raspberry Pi'
  const defaultPort = isUnityMode ? 8000 : 5051
  const runtimeLabel = status?.runtimeLabel ?? (isUnityMode ? 'Keyestudio KS0223 (Unity Simulator)' : 'Keyestudio KS0223 (Real Robot)')
  const tracks = unityCatalog?.tracks ?? []
  const vehicles = unityCatalog?.vehicles ?? []

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
    if (!unityCatalog || !unityCatalog.selectedVehicleId) {
      return 'не выбрана'
    }

    return (
      unityCatalog.vehicles.find((item) => item.id === unityCatalog.selectedVehicleId)?.displayName ?? unityCatalog.selectedVehicleId
    )
  }, [unityCatalog])

  useEffect(() => {
    if (!unityDialogOpen) {
      return
    }

    if (unityCatalog?.selectedTrackId) {
      setTrackDraft(unityCatalog.selectedTrackId)
    }

    if (unityCatalog?.selectedVehicleId) {
      setVehicleDraft(unityCatalog.selectedVehicleId)
    }
  }, [unityCatalog, unityDialogOpen])

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
    if (!trackDraft || !vehicleDraft) {
      setUnityDialogError('Выбери трек и машинку')
      return
    }

    setUnityDialogBusy(true)
    setUnityDialogError(null)
    try {
      await onUnitySelectionSave(trackDraft, vehicleDraft, connectApplyImmediately)
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
            helperText={`Текущий порт: ${status?.targetPort ?? defaultPort}`}
          />

          {isUnityMode ? (
            <Stack spacing={1}>
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                <Chip label={`Трек: ${selectedTrackTitle}`} size="small" />
                <Chip label={`Машинка: ${selectedVehicleTitle}`} size="small" />
              </Stack>
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
              Выбранный трек и машинка будут применяться при следующем подключении. Если соединение уже активно, настройки
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
              label="Машинка"
              value={vehicleDraft}
              onChange={(event) => setVehicleDraft(event.target.value)}
              disabled={dialogBusy || vehicles.length === 0}
            >
              {vehicles.map((vehicle) => (
                <MenuItem key={vehicle.id} value={vehicle.id}>
                  {vehicle.displayName}
                </MenuItem>
              ))}
            </TextField>

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
            disabled={dialogBusy || !trackDraft || !vehicleDraft}
          >
            Применить
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  )
}
