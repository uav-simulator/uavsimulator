import BoltIcon from '@mui/icons-material/Bolt'
import LinkOffIcon from '@mui/icons-material/LinkOff'
import PortableWifiOffIcon from '@mui/icons-material/PortableWifiOff'
import SensorsIcon from '@mui/icons-material/Sensors'
import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import type { StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  busy: boolean
  targetHost: string
  onTargetHostChange: (value: string) => void
  onConnect: () => Promise<void>
  onDisconnect: () => Promise<void>
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
  targetHost,
  onTargetHostChange,
  onConnect,
  onDisconnect,
}: Props) {
  const tcpConnected = status?.tcpConnected ?? false

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
              label={tcpConnected ? 'TCP подключен' : 'TCP отключен'}
            />
            <Chip label={`UI-клиенты: ${status?.uiConnectedClients ?? 0}`} variant="outlined" />
            <Chip label={`Задержка: ${formatLatency(status?.latencyMs ?? null)}`} variant="outlined" />
          </Stack>

          <Typography variant="body2" color={status?.lastError ? 'error.main' : 'text.secondary'}>
            Последняя ошибка: {status?.lastError ?? 'нет'}
          </Typography>

          <TextField
            size="small"
            label="IP или host Raspberry Pi"
            value={targetHost}
            onChange={(event) => onTargetHostChange(event.target.value)}
            disabled={busy}
            placeholder="192.168.1.121"
            helperText={`Порт: ${status?.targetPort ?? 5051}`}
          />

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
    </Card>
  )
}
