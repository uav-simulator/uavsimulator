import { Alert, Card, CardContent, Chip, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import type { CameraStatusDto, SensorBridgeStatusDto, SensorTelemetryDto, StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  camera: CameraStatusDto | null
  sensorStatus: SensorBridgeStatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
}

type SensorItem = {
  key: string
  source: string
  networkStatus: 'available' | 'partial' | 'unavailable'
  notes: string
}

function statusChip(value: SensorItem['networkStatus']) {
  if (value === 'available') {
    return <Chip size="small" color="success" label="Доступно" />
  }

  if (value === 'partial') {
    return <Chip size="small" color="warning" label="Частично" />
  }

  return <Chip size="small" label="Недоступно" variant="outlined" />
}

export function SensorInventoryPanel({ status, camera, sensorStatus, sensorTelemetry }: Props) {
  const cameraVideoStatus: SensorItem['networkStatus'] = camera?.hasFrame ? 'available' : 'partial'
  const flat = sensorTelemetry?.flat ?? {}
  const hasUltrasonic = sensorStatus?.hasTelemetry && Boolean(flat['ultrasonic.distance_cm'])
  const hasTracking =
    sensorStatus?.hasTelemetry &&
    (flat['tracking.left'] !== undefined || flat['tracking.center'] !== undefined || flat['tracking.right'] !== undefined)
  const hasIr = sensorStatus?.hasTelemetry && (flat['ir.last_code_hex'] || flat['ir.signal_level']) !== undefined

  const sensors: SensorItem[] = [
    {
      key: 'Камера RGB (video)',
      source: 'FramesSend.py / FramesSend_test.py',
      networkStatus: cameraVideoStatus,
      notes: camera?.hasFrame ? 'Поток получен через UDP 5051.' : 'Поток не обнаружен (проверь запуск FramesSend.py на Pi).',
    },
    {
      key: 'Пан/тилт камеры (2 сервопривода)',
      source: 'MainControl.py (CamUp/CamDown/CamLeft/CamRight/CamStop)',
      networkStatus: status?.tcpConnected ? 'available' : 'partial',
      notes: 'Управление доступно через TCP команды Cam*.',
    },
    {
      key: 'Ультразвуковой датчик расстояния',
      source: 'basic_project/bp3_ultrasonic.py',
      networkStatus: hasUltrasonic ? 'available' : sensorStatus?.enabled ? 'partial' : 'unavailable',
      notes: hasUltrasonic
        ? `distance_cm=${flat['ultrasonic.distance_cm'] ?? 'n/a'}, left/center/right=${flat['ultrasonic.scan.left_cm'] ?? '-'} / ${flat['ultrasonic.scan.center_cm'] ?? '-'} / ${flat['ultrasonic.scan.right_cm'] ?? '-'}`
        : 'Через MainControl.py недоступно, но доступно через pi-telemetry-addon.',
    },
    {
      key: 'Датчики линии (tracking)',
      source: 'basic_project/bp2_tracking.py / bp10_tracking_car.py',
      networkStatus: hasTracking ? 'available' : sensorStatus?.enabled ? 'partial' : 'unavailable',
      notes: hasTracking
        ? `left/center/right=${flat['tracking.left'] ?? '-'} / ${flat['tracking.center'] ?? '-'} / ${flat['tracking.right'] ?? '-'}`
        : 'Через MainControl.py недоступно, но доступно через pi-telemetry-addon.',
    },
    {
      key: 'IR receiver / пульт',
      source: 'basic_project/bp8_ir_remote.py / bp9_ir_car.py',
      networkStatus: hasIr ? 'available' : sensorStatus?.enabled ? 'partial' : 'unavailable',
      notes: hasIr
        ? `last=${flat['ir.last_code_hex'] ?? 'n/a'} (seen ${flat['ir.last_seen_at'] ?? 'n/a'})`
        : 'IR код появится после нажатия кнопки на пульте.',
    },
    {
      key: 'OLED / LED matrix / buzzer',
      source: 'OledModule/*, basic_project/bp5_*, bp1_*',
      networkStatus: 'unavailable',
      notes: 'Это локальные исполнительные устройства, телеметрия наружу не уходит.',
    },
  ]

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Typography variant="h6">Сенсоры KS0223 и доступность данных</Typography>

          <Alert severity="info">
            Ниже показан реальный статус доступности. Для всех GPIO-сенсоров используется отдельный pi-telemetry-addon
            (HTTP JSON endpoint на Pi).
          </Alert>

          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Сенсор/модуль</TableCell>
                <TableCell>Источник в коде Pi</TableCell>
                <TableCell>По сети сейчас</TableCell>
                <TableCell>Комментарий</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {sensors.map((sensor) => (
                <TableRow key={sensor.key}>
                  <TableCell>{sensor.key}</TableCell>
                  <TableCell>{sensor.source}</TableCell>
                  <TableCell>{statusChip(sensor.networkStatus)}</TableCell>
                  <TableCell>{sensor.notes}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Stack>
      </CardContent>
    </Card>
  )
}
