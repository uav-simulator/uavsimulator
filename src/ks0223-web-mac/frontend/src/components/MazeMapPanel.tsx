import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Card, CardContent, Chip, Stack, Typography } from '@mui/material'
import type { AutopilotPreviewDto } from '../api'
import type { AutopilotStatusDto, ModelBindingDto, SensorTelemetryDto } from '../types'

type PoseSample = {
  x: number
  z: number
  yawDeg: number
  speedMps: number | null
  timestamp: string
}

type ParsedTelemetry = {
  pose: PoseSample | null
  route: Record<string, string>
  sensor: Record<string, string>
  reward: number | null
}

type Props = {
  sensorTelemetry: SensorTelemetryDto | null
  selectedTrackId?: string
  modelViewUrl?: string
  modelViewAgentId?: string
  policyPreview?: AutopilotPreviewDto | null
  autopilot?: AutopilotStatusDto | null
  modelBinding?: ModelBindingDto | null
  selectedModelId?: string | null
  embedded?: boolean
}

type OccupancyState = {
  grid: Float32Array
  trail: PoseSample[]
}

const MAZE_TRACK_ID = 'track.cardboard_maze.v1'
const CELL_M = 0.225
const GRID_SIZE = 80
const GRID_ORIGIN = 40
const EGO_SIZE = 21
const EGO_HALF = 10
const FREE_INCREMENT = 0.20
const WALL_INCREMENT = 0.40
const ULTRASONIC_MAX_M = 2.0
const POLICY_ACTION_NAMES = ['DirStop', 'DirForward', 'DirBack', 'DirLeft', 'DirRight']
const POLICY_ACTION_LABELS = ['Стоп', 'Вперед', 'Назад', 'Влево', 'Вправо']
const DISTANCE_LABELS = ['F', 'FR', 'R', 'BR', 'B', 'BL', 'L', 'FL']
const DISTANCE_OFFSETS_DEG = [0, 45, 90, 135, 180, -135, -90, -45]
const ACTION_COLORS = ['#9b9b9b', '#58d68d', '#dc5a5a', '#f4ac45', '#60a5fa']

function emptyOccupancy(): OccupancyState {
  return { grid: new Float32Array(3 * GRID_SIZE * GRID_SIZE), trail: [] }
}

function gridIndex(channel: number, gx: number, gz: number): number {
  return channel * GRID_SIZE * GRID_SIZE + gz * GRID_SIZE + gx
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function asString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function asNumber(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string') {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : null
  }
  return null
}

function readEntryMap(value: unknown): Record<string, string> {
  if (!Array.isArray(value)) {
    return {}
  }

  const entries: Record<string, string> = {}
  value.forEach((item) => {
    const record = asRecord(item)
    const key = asString(record?.key)
    const entryValue = asString(record?.value)
    if (key && entryValue !== null) {
      entries[key] = entryValue
    }
  })
  return entries
}

function yawDegFromQuaternion(value: unknown): number {
  const rotation = asRecord(value)
  const x = asNumber(rotation?.x) ?? 0
  const y = asNumber(rotation?.y) ?? 0
  const z = asNumber(rotation?.z) ?? 0
  const w = asNumber(rotation?.w) ?? 1
  const sinYaw = 2 * (w * y + x * z)
  const cosYaw = 1 - 2 * (y * y + z * z)
  const raw = Math.atan2(sinYaw, cosYaw) * 180 / Math.PI
  return (raw + 360) % 360
}

function parseTelemetry(sensorTelemetry: SensorTelemetryDto | null): ParsedTelemetry {
  if (!sensorTelemetry?.rawJson) {
    return { pose: null, route: {}, sensor: sensorTelemetry?.flat ?? {}, reward: null }
  }

  try {
    const root = asRecord(JSON.parse(sensorTelemetry.rawJson))
    const state = asRecord(root?.state)
    const pose = asRecord(state?.pose)
    const position = asRecord(pose?.position)
    const x = asNumber(position?.x)
    const z = asNumber(position?.z)
    const telemetryMap = readEntryMap(state?.telemetry)
    const speedMps = asNumber(state?.speed)

    return {
      pose: x === null || z === null
        ? null
        : {
          x,
          z,
          yawDeg: yawDegFromQuaternion(pose?.rotation),
          speedMps,
          timestamp: sensorTelemetry.timestamp,
        },
      route: readEntryMap(root?.info),
      sensor: { ...sensorTelemetry.flat, ...telemetryMap },
      reward: asNumber(root?.reward),
    }
  } catch {
    return { pose: null, route: {}, sensor: sensorTelemetry.flat, reward: null }
  }
}

function worldToGrid(worldX: number, worldZ: number): { gx: number; gz: number } {
  return {
    gx: Math.round(worldX / CELL_M) + GRID_ORIGIN,
    gz: Math.round(worldZ / CELL_M) + GRID_ORIGIN,
  }
}

function clamp01(value: number): number {
  return Math.max(0, Math.min(1, value))
}

function updateOccupancy(prev: OccupancyState, pose: PoseSample, frontM: number): OccupancyState {
  const last = prev.trail[prev.trail.length - 1]
  if (last && last.timestamp === pose.timestamp && Math.hypot(last.x - pose.x, last.z - pose.z) < 0.005) {
    return prev
  }

  const routeJustReset = prev.trail.length > 6 &&
    Math.hypot(pose.x, pose.z) < 0.18 &&
    Math.hypot(prev.trail[prev.trail.length - 1].x, prev.trail[prev.trail.length - 1].z) > 0.45
  const grid = routeJustReset ? new Float32Array(prev.grid.length) : new Float32Array(prev.grid)
  const { gx, gz } = worldToGrid(pose.x, pose.z)
  if (gx >= 0 && gx < GRID_SIZE && gz >= 0 && gz < GRID_SIZE) {
    grid[gridIndex(0, gx, gz)] = 1
  }

  const dTotal = Math.min(Math.max(frontM, 0), ULTRASONIC_MAX_M)
  const yawRad = pose.yawDeg * Math.PI / 180
  const dx = Math.sin(yawRad)
  const dz = Math.cos(yawRad)
  const stepM = CELL_M * 0.5
  const steps = Math.max(1, Math.ceil(dTotal / stepM))
  let lastCell: string | null = null
  for (let k = 1; k <= steps; k += 1) {
    const t = Math.min(k * stepM, dTotal)
    const cell = worldToGrid(pose.x + dx * t, pose.z + dz * t)
    if (cell.gx < 0 || cell.gx >= GRID_SIZE || cell.gz < 0 || cell.gz >= GRID_SIZE) {
      break
    }

    const key = `${cell.gx}:${cell.gz}`
    if (t >= dTotal) {
      if (frontM < ULTRASONIC_MAX_M - 0.001) {
        const idx = gridIndex(2, cell.gx, cell.gz)
        grid[idx] = Math.min(1, grid[idx] + WALL_INCREMENT)
      } else if (key !== lastCell) {
        const idx = gridIndex(1, cell.gx, cell.gz)
        grid[idx] = Math.min(1, grid[idx] + FREE_INCREMENT)
      }
      break
    }

    if (key === lastCell) {
      continue
    }
    lastCell = key
    const idx = gridIndex(1, cell.gx, cell.gz)
    grid[idx] = Math.min(1, grid[idx] + FREE_INCREMENT)
  }

  return {
    grid,
    trail: [...(routeJustReset ? [] : prev.trail), pose].slice(-260),
  }
}

function egoWindow(grid: Float32Array, pose: PoseSample): Array<{ r: number; g: number; b: number }> {
  const out: Array<{ r: number; g: number; b: number }> = []
  const center = worldToGrid(pose.x, pose.z)
  const cosY = Math.cos(pose.yawDeg * Math.PI / 180)
  const sinY = Math.sin(pose.yawDeg * Math.PI / 180)
  for (let ez = EGO_SIZE - 1; ez >= 0; ez -= 1) {
    const forwardOffset = ez - EGO_HALF
    for (let ex = 0; ex < EGO_SIZE; ex += 1) {
      const rightOffset = ex - EGO_HALF
      const wxOff = forwardOffset * sinY + rightOffset * cosY
      const wzOff = forwardOffset * cosY - rightOffset * sinY
      const gx = center.gx + Math.round(wxOff)
      const gz = center.gz + Math.round(wzOff)
      if (gx < 0 || gx >= GRID_SIZE || gz < 0 || gz >= GRID_SIZE) {
        out.push({ r: 0, g: 0, b: 0 })
        continue
      }
      out.push({
        r: grid[gridIndex(2, gx, gz)],
        g: grid[gridIndex(1, gx, gz)],
        b: grid[gridIndex(0, gx, gz)],
      })
    }
  }
  return out
}

function distanceFromMap(grid: Float32Array, pose: PoseSample, offsetDeg: number): number {
  const yawRad = (pose.yawDeg + offsetDeg) * Math.PI / 180
  const dx = Math.sin(yawRad)
  const dz = Math.cos(yawRad)
  const stepM = 0.05
  const maxSteps = Math.floor(ULTRASONIC_MAX_M / stepM)
  for (let k = 1; k <= maxSteps; k += 1) {
    const t = k * stepM
    const { gx, gz } = worldToGrid(pose.x + dx * t, pose.z + dz * t)
    if (gx < 0 || gx >= GRID_SIZE || gz < 0 || gz >= GRID_SIZE) {
      return 1
    }
    if (grid[gridIndex(2, gx, gz)] > 0.2) {
      return clamp01(t / ULTRASONIC_MAX_M)
    }
  }
  return 1
}

function formatPercent(value: number): string {
  return `${Math.round(clamp01(value) * 100)}%`
}

function estimateFinalAction(
  rawAction: string | null | undefined,
  frontM: number | null | undefined,
  autopilot: AutopilotStatusDto | null | undefined,
): { finalAction: string | null; rule: string } {
  if (autopilot?.isRunning && autopilot.lastCommand) {
    return { finalAction: autopilot.lastCommand, rule: 'live autopilot command' }
  }
  if (!rawAction) {
    return { finalAction: null, rule: 'waiting for policy preview' }
  }
  if (rawAction === 'DirForward' && frontM != null && frontM < 0.10) {
    return { finalAction: 'DirStop', rule: 'safety: front < 0.10 m' }
  }
  return { finalAction: rawAction, rule: 'raw action accepted' }
}

function probabilityAt(preview: AutopilotPreviewDto | null | undefined, index: number): number {
  return preview?.probabilities?.[index] ?? 0
}

export function MazeMapPanel({
  sensorTelemetry,
  selectedTrackId,
  modelViewUrl,
  modelViewAgentId,
  policyPreview,
  autopilot,
  modelBinding,
  selectedModelId,
  embedded = false,
}: Props) {
  const parsed = useMemo(() => parseTelemetry(sensorTelemetry), [sensorTelemetry])
  const [occupancy, setOccupancy] = useState<OccupancyState>(() => emptyOccupancy())
  const isMazeSelected = selectedTrackId === MAZE_TRACK_ID || !selectedTrackId
  const frontM = policyPreview?.frontUltrasonicM ?? asNumber(parsed.sensor['sensor.ultrasonic.front.m']) ?? ULTRASONIC_MAX_M
  const { finalAction, rule } = estimateFinalAction(policyPreview?.chosenAction, frontM, autopilot)
  const progress = parsed.route['route.current_index'] && parsed.route['route.total_waypoints']
    ? Number(parsed.route['route.current_index']) / Math.max(1, Number(parsed.route['route.total_waypoints']))
    : null

  useEffect(() => {
    setOccupancy(emptyOccupancy())
  }, [selectedTrackId])

  useEffect(() => {
    if (!parsed.pose || !isMazeSelected) {
      return
    }

    setOccupancy((prev) => updateOccupancy(prev, parsed.pose!, frontM))
  }, [frontM, isMazeSelected, parsed.pose])

  const occupancyCells = parsed.pose ? egoWindow(occupancy.grid, parsed.pose) : []
  const distanceValues = parsed.pose
    ? DISTANCE_OFFSETS_DEG.map((offset, index) => {
      const fromMap = distanceFromMap(occupancy.grid, parsed.pose!, offset)
      return index === 0 ? clamp01(frontM / ULTRASONIC_MAX_M) : fromMap
    })
    : DISTANCE_OFFSETS_DEG.map(() => 1)
  const routeProgress = parsed.route['route.current_index'] && parsed.route['route.total_waypoints']
    ? `${parsed.route['route.current_index']}/${parsed.route['route.total_waypoints']}`
    : 'n/a'
  const problem = autopilot?.lastError || autopilot?.stopReason || null
  const hasProbabilityPreview = Boolean(policyPreview?.ok && policyPreview.probabilities)
  const previewProblem = policyPreview && !policyPreview.ok ? policyPreview.reason || 'preview unavailable' : null
  const selectedModelLabel = modelBinding
    ? `${modelBinding.name} ${modelBinding.version}`
    : policyPreview?.modelId || selectedModelId || 'модель не привязана'
  const finalActionColor = ACTION_COLORS[Math.max(0, POLICY_ACTION_NAMES.indexOf(finalAction ?? 'DirStop'))]

  const content = (
    <Stack spacing={1.5}>
          <Stack direction="row" spacing={1} alignItems="center" justifyContent="space-between" flexWrap="wrap" useFlexGap>
            <Box sx={{ minWidth: 0 }}>
              <Typography variant="h6">Вижен модели</Typography>
              <Typography variant="body2" color="text.secondary">
                Камера, карта памяти, контекст и решение политики в реальном времени.
              </Typography>
            </Box>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              <Chip size="small" label={`камера агента: ${modelViewAgentId || 'n/a'}`} color="info" variant="outlined" />
              <Chip size="small" label={`маршрут: ${routeProgress}`} variant="outlined" />
              <Chip size="small" label={`до стены: ${frontM.toFixed(2)} м`} variant="outlined" />
              <Chip
                size="small"
                label={`команда: ${finalAction ?? 'n/a'}`}
                color={finalAction === 'DirStop' ? 'warning' : 'default'}
                variant="outlined"
              />
            </Stack>
          </Stack>

          {problem ? (
            <Alert severity={autopilot?.lastError ? 'error' : 'warning'} variant="outlined">
              Автопилот остановлен: {problem}. Ниже видно, что получает политика и какое правило меняет команду.
            </Alert>
          ) : null}

          {previewProblem ? (
            <Alert severity="info" variant="outlined">
              Preview политики пока недоступен: {previewProblem}. Камера и карта продолжают обновляться; вероятности появятся после привязки модели и активного shadow preview.
            </Alert>
          ) : null}

          {!parsed.pose ? (
            <Alert severity="info">Жду telemetry snapshot от Unity runtime.</Alert>
          ) : (
            <Box
              sx={{
                bgcolor: '#121418',
                border: '1px solid rgba(230,230,230,0.28)',
                borderRadius: 1,
                p: 1.5,
                overflow: 'hidden',
              }}
            >
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: { xs: '1fr', lg: 'minmax(320px, 0.95fr) minmax(360px, 1.05fr)' },
                  gap: 2,
                  alignItems: 'start',
                }}
              >
                <Stack spacing={1} sx={{ minWidth: 0 }}>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    <Typography variant="caption" color="grey.100">
                      Вход модели: камера 84x84
                    </Typography>
                    <Chip size="small" label={selectedModelLabel} variant="outlined" sx={{ maxWidth: '100%' }} />
                  </Stack>
                  <Box
                    sx={{
                      width: '100%',
                      height: { xs: 260, sm: 320, lg: 360 },
                      maxHeight: 380,
                      border: '2px solid rgba(230,230,230,0.86)',
                      borderRadius: 1,
                      overflow: 'hidden',
                      bgcolor: '#05070a',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    {modelViewUrl ? (
                      <Box
                        component="img"
                        src={`${modelViewUrl}&_=${encodeURIComponent(sensorTelemetry?.timestamp ?? '')}`}
                        alt="model input 84x84"
                        sx={{
                          width: '100%',
                          height: '100%',
                          imageRendering: 'pixelated',
                          objectFit: 'cover',
                          display: 'block',
                        }}
                      />
                    ) : (
                      <Typography variant="caption" color="text.secondary">нет endpoint входа модели</Typography>
                    )}
                  </Box>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    <Chip size="small" label={`step=${autopilot?.stepsTotal ?? 0}`} />
                    <Chip size="small" label={`progress=${progress == null || Number.isNaN(progress) ? 'n/a' : progress.toFixed(3)}`} />
                    <Chip size="small" label={`reward=${parsed.reward?.toFixed(1) ?? 'n/a'}`} />
                    <Chip size="small" label={`скорость=${parsed.pose.speedMps?.toFixed(3) ?? 'n/a'} м/с`} />
                  </Stack>
                </Stack>

                <Stack spacing={1.5} sx={{ minWidth: 0 }}>
                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr', sm: 'minmax(190px, 240px) minmax(0, 1fr)' },
                      gap: 1.5,
                      alignItems: 'start',
                      minWidth: 0,
                    }}
                  >
                    <Stack spacing={1} sx={{ minWidth: 0 }}>
                      <Typography variant="caption" color="grey.100">
                        Карта памяти
                      </Typography>
                      <Box
                        sx={{
                          width: '100%',
                          maxWidth: 240,
                          aspectRatio: '1 / 1',
                          display: 'grid',
                          gridTemplateColumns: `repeat(${EGO_SIZE}, 1fr)`,
                          gridTemplateRows: `repeat(${EGO_SIZE}, 1fr)`,
                          border: '2px solid rgba(230,230,230,0.86)',
                          borderRadius: 1,
                          overflow: 'hidden',
                          bgcolor: '#000',
                        }}
                      >
                        {occupancyCells.map((cell, index) => (
                          <Box
                            key={index}
                            sx={{
                              bgcolor: `rgb(${Math.round(cell.r * 255)}, ${Math.round(cell.g * 255)}, ${Math.round(cell.b * 255)})`,
                            }}
                          />
                        ))}
                      </Box>
                    </Stack>

                    <Stack spacing={0.75} sx={{ minWidth: 0 }}>
                      <Typography variant="caption" color="grey.100">Контекст 8 лучей</Typography>
                      {DISTANCE_LABELS.map((label, index) => {
                        const value = distanceValues[index]
                        return (
                          <Box key={label} sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 54px', alignItems: 'center', gap: 1 }}>
                            <Box sx={{ width: '100%', height: 14, border: '1px solid rgba(220,220,220,0.7)', bgcolor: '#1d2025' }}>
                              <Box sx={{ height: '100%', width: formatPercent(value), bgcolor: '#59aeff' }} />
                            </Box>
                            <Typography variant="caption" color="grey.100">{label} {value.toFixed(2)}</Typography>
                          </Box>
                        )
                      })}
                    </Stack>
                  </Box>

                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr', md: 'minmax(260px, 1fr) minmax(190px, 0.82fr)' },
                      gap: 1.5,
                      minWidth: 0,
                    }}
                  >
                    <Stack spacing={0.75} sx={{ minWidth: 0 }}>
                      <Typography variant="caption" color="grey.100">Вероятности действий</Typography>
                      {hasProbabilityPreview ? (
                        POLICY_ACTION_NAMES.map((name, index) => {
                          const probability = probabilityAt(policyPreview, index)
                          const chosen = policyPreview?.chosenIndex === index
                          return (
                            <Box key={name} sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(92px, auto)', alignItems: 'center', gap: 1 }}>
                              <Box sx={{ width: '100%', height: 16, border: chosen ? '1px solid #fff' : '1px solid #5c5c5c', bgcolor: '#1d2025' }}>
                                <Box sx={{ height: '100%', width: formatPercent(probability), bgcolor: ACTION_COLORS[index] }} />
                              </Box>
                              <Typography variant="caption" color="grey.100" sx={{ whiteSpace: 'nowrap' }}>
                                {POLICY_ACTION_LABELS[index]} {probability.toFixed(2)}
                              </Typography>
                            </Box>
                          )
                        })
                      ) : (
                        <Alert severity="info" variant="outlined" sx={{ py: 0.5 }}>
                          Вероятности появятся после `Bind` и активного Show decisions.
                        </Alert>
                      )}
                    </Stack>

                    <Box
                      sx={{
                        minHeight: 142,
                        border: '2px solid rgba(230,230,230,0.86)',
                        borderRadius: 1,
                        bgcolor: '#1c2026',
                        p: 1.5,
                        minWidth: 0,
                      }}
                    >
                      <Stack spacing={0.75}>
                        <Typography variant="caption" color="grey.100" sx={{ overflowWrap: 'anywhere' }}>
                          модель: {selectedModelLabel}
                        </Typography>
                        <Typography variant="caption" color="grey.100">raw: {policyPreview?.chosenAction ?? 'n/a'}</Typography>
                        <Typography variant="caption" sx={{ color: finalActionColor, fontWeight: 700 }}>
                          итог: {finalAction ?? 'n/a'}
                        </Typography>
                        <Typography variant="caption" color="grey.100" sx={{ overflowWrap: 'anywhere' }}>
                          {policyPreview?.guardReason ?? rule}
                        </Typography>
                        <Typography variant="caption" color="grey.100">до стены: {frontM.toFixed(2)} м</Typography>
                      </Stack>
                    </Box>
                  </Box>
                </Stack>
              </Box>
            </Box>
          )}
    </Stack>
  )

  if (embedded) {
    return (
      <Box
        sx={{
          border: '1px solid',
          borderColor: 'divider',
          borderRadius: 2,
          p: { xs: 1.5, md: 2 },
          bgcolor: 'rgba(6, 15, 25, 0.34)',
        }}
      >
        {content}
      </Box>
    )
  }

  return (
    <Card>
      <CardContent>{content}</CardContent>
    </Card>
  )
}
