import {
  Card,
  CardContent,
  Chip,
  Divider,
  List,
  ListItem,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material'
import type { IncomingMessageDto, SensorBridgeStatusDto, SensorTelemetryDto, StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  incoming: IncomingMessageDto[]
  sensorStatus: SensorBridgeStatusDto | null
  sensorTelemetry: SensorTelemetryDto | null
}

export function TelemetryPanel({ status, incoming, sensorStatus, sensorTelemetry }: Props) {
  const latestTelemetry = [...incoming]
    .reverse()
    .find((item) => item.parsedTelemetry && Object.keys(item.parsedTelemetry).length > 0)
  const flat = sensorTelemetry?.flat ?? {}
  const isUnityMode = status?.runtimeMode === 'unity-sim'
  const runtimeTelemetryLabel = isUnityMode ? 'Unity telemetry bridge: активен' : 'Pi sensor bridge: включен'
  const runtimeTelemetryDisabledLabel = isUnityMode ? 'Unity telemetry bridge: выключен' : 'Pi sensor bridge: выключен'
  const runtimeDescription = isUnityMode
    ? 'В режиме unity-sim телеметрия приходит из HTTP API симулятора и нормализуется backend-адаптером в те же поля, что и для физического KS0223.'
    : 'Что означает каждый сенсор: HC-SR04 измеряет расстояние до препятствия в сантиметрах; Scan L/C/R показывает замеры слева/по центру/справа при повороте ультразвукового модуля; Tracking (L/C/R) это три датчика линии (0/1) для следования по линии; IR показывает последний код с ИК-пульта; CPU temp и uptime нужны для контроля состояния Raspberry Pi.'
  const tcpTelemetryUnavailableText = isUnityMode
    ? 'Низкоуровневые TCP-сообщения MainControl.py в режиме unity-sim отсутствуют. Ниже отображается унифицированная телеметрия симулятора.'
    : 'TCP-телеметрия от MainControl.py недоступна в текущем протоколе.'
  const sensorValuesTitle = isUnityMode ? 'Последние значения из unified telemetry' : 'Последние значения из sensor bridge'
  const incomingTitle = isUnityMode ? 'Последние сообщения runtime' : 'Входящие TCP сообщения'

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Typography variant="h6">Телеметрия и данные сенсоров</Typography>

          <Typography variant="body2" color="text.secondary">
            {runtimeDescription}
          </Typography>

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Chip
              label={sensorStatus?.enabled ? runtimeTelemetryLabel : runtimeTelemetryDisabledLabel}
              color={sensorStatus?.enabled ? 'success' : 'default'}
              variant={sensorStatus?.enabled ? 'filled' : 'outlined'}
            />
            <Chip label={`HC-SR04: ${flat['ultrasonic.distance_cm'] ?? 'n/a'} cm`} variant="outlined" />
            <Chip
              label={`Tracking: ${flat['tracking.left'] ?? '-'} / ${flat['tracking.center'] ?? '-'} / ${flat['tracking.right'] ?? '-'}`}
              variant="outlined"
            />
            <Chip label={`IR: ${flat['ir.last_code_hex'] ?? 'n/a'}`} variant="outlined" />
          </Stack>

          {sensorStatus?.enabled && sensorStatus.lastError ? (
            <Typography variant="body2" color="warning.main">
              Sensor bridge warning: {sensorStatus.lastError}
            </Typography>
          ) : null}

          {!status?.hasParsedTelemetry || !latestTelemetry?.parsedTelemetry ? (
            <Typography variant="body1" color="text.secondary">
              {tcpTelemetryUnavailableText}
            </Typography>
          ) : (
            <List dense>
              {Object.entries(latestTelemetry.parsedTelemetry).map(([key, value]) => (
                <ListItem key={key} disablePadding>
                  <ListItemText primary={key} secondary={value} />
                </ListItem>
              ))}
            </List>
          )}

          <Divider />
          <Typography variant="subtitle2" color="text.secondary">
            {sensorValuesTitle}
          </Typography>
          {!sensorTelemetry ? (
            <Typography variant="body2" color="text.secondary">
              Данные пока не получены.
            </Typography>
          ) : (
            <List dense>
              {Object.entries(flat)
                .slice(0, 24)
                .map(([key, value]) => (
                  <ListItem key={key} disablePadding>
                    <ListItemText primary={key} secondary={value} />
                  </ListItem>
                ))}
            </List>
          )}

          <Divider />

          <Typography variant="subtitle2" color="text.secondary">
            {incomingTitle}
          </Typography>
          <List dense>
            {[...incoming].reverse().slice(0, 12).map((item, index) => (
              <ListItem key={`${item.timestamp}-${index}`} disablePadding>
                <ListItemText
                  primary={item.message || '<empty>'}
                  secondary={new Date(item.timestamp).toLocaleTimeString()}
                />
              </ListItem>
            ))}
          </List>
        </Stack>
      </CardContent>
    </Card>
  )
}
