import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  FormControlLabel,
  Stack,
  Switch,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { SensorTelemetryDto } from '../types'

type Props = {
  sensorTelemetry: SensorTelemetryDto | null
  onSetPattern: (pattern: string) => Promise<void>
  onSetCustomFrame: (frameHex: string) => Promise<void>
  onClear: () => Promise<void>
}

const patterns = ['smile', 'forward', 'back', 'left', 'right', 'stop', 'heart'] as const
const GRID_ROWS = 8
const GRID_COLS = 16
const LED_MIRROR_STORAGE_KEY = 'ks0223_led_mirror_settings_v1'

type Matrix = boolean[][]
type MirrorSettings = {
  flipHorizontal: boolean
  flipVertical: boolean
}

const defaultMirrorSettings: MirrorSettings = {
  flipHorizontal: false,
  flipVertical: false,
}

function createEmptyMatrix(): Matrix {
  return Array.from({ length: GRID_ROWS }, () => Array.from({ length: GRID_COLS }, () => false))
}

function parseFrameToBytes(frameHex: string): number[] | null {
  const cleaned = frameHex.replace(/[,;|]/g, ' ')
  let parts = cleaned
    .trim()
    .split(/\s+/)
    .filter(Boolean)

  if (parts.length === 1 && parts[0].length === 32) {
    parts = parts[0].match(/.{1,2}/g) ?? []
  }

  if (parts.length !== GRID_COLS) {
    return null
  }

  const bytes: number[] = []
  for (const part of parts) {
    const normalized = part.toLowerCase().startsWith('0x') ? part.slice(2) : part
    const value = Number.parseInt(normalized, 16)
    if (Number.isNaN(value) || value < 0 || value > 255) {
      return null
    }
    bytes.push(value)
  }

  return bytes
}

function bytesToMatrix(bytes: number[]): Matrix {
  const matrix = createEmptyMatrix()
  for (let col = 0; col < GRID_COLS; col += 1) {
    const value = bytes[col] ?? 0
    for (let row = 0; row < GRID_ROWS; row += 1) {
      matrix[row][col] = ((value >> row) & 0x01) === 1
    }
  }
  return matrix
}

function transformIndex(row: number, col: number, settings: MirrorSettings): { row: number; col: number } {
  const transformedRow = settings.flipVertical ? GRID_ROWS - 1 - row : row
  const transformedCol = settings.flipHorizontal ? GRID_COLS - 1 - col : col
  return { row: transformedRow, col: transformedCol }
}

function toWireMatrix(editorMatrix: Matrix, settings: MirrorSettings): Matrix {
  const wire = createEmptyMatrix()
  for (let row = 0; row < GRID_ROWS; row += 1) {
    for (let col = 0; col < GRID_COLS; col += 1) {
      const { row: r, col: c } = transformIndex(row, col, settings)
      wire[r][c] = editorMatrix[row][col]
    }
  }
  return wire
}

function fromWireMatrix(wireMatrix: Matrix, settings: MirrorSettings): Matrix {
  const editor = createEmptyMatrix()
  for (let row = 0; row < GRID_ROWS; row += 1) {
    for (let col = 0; col < GRID_COLS; col += 1) {
      const { row: r, col: c } = transformIndex(row, col, settings)
      editor[row][col] = wireMatrix[r][c]
    }
  }
  return editor
}

function loadMirrorSettings(): MirrorSettings {
  if (typeof window === 'undefined') {
    return defaultMirrorSettings
  }

  try {
    const raw = window.localStorage.getItem(LED_MIRROR_STORAGE_KEY)
    if (!raw) {
      return defaultMirrorSettings
    }

    const parsed = JSON.parse(raw) as Partial<MirrorSettings>
    return {
      flipHorizontal: Boolean(parsed.flipHorizontal),
      flipVertical: Boolean(parsed.flipVertical),
    }
  } catch {
    return defaultMirrorSettings
  }
}

function matrixToBytes(matrix: Matrix): number[] {
  const bytes = new Array<number>(GRID_COLS).fill(0)
  for (let col = 0; col < GRID_COLS; col += 1) {
    let value = 0
    for (let row = 0; row < GRID_ROWS; row += 1) {
      if (matrix[row][col]) {
        value |= 1 << row
      }
    }
    bytes[col] = value
  }

  return bytes
}

function bytesToHex(bytes: number[]): string {
  return bytes.map((value) => value.toString(16).toUpperCase().padStart(2, '0')).join(' ')
}

export function LedPanel({ sensorTelemetry, onSetPattern, onSetCustomFrame, onClear }: Props) {
  const [selectedPattern, setSelectedPattern] = useState<string>('smile')
  const [customFrame, setCustomFrame] = useState('00 00 38 40 40 40 3A 02 02 3A 40 40 40 38 00 00')
  const [matrix, setMatrix] = useState<Matrix>(() => {
    const parsed = parseFrameToBytes('00 00 38 40 40 40 3A 02 02 3A 40 40 40 38 00 00')
    return parsed ? bytesToMatrix(parsed) : createEmptyMatrix()
  })
  const [busy, setBusy] = useState(false)
  const [lastResult, setLastResult] = useState<string | null>(null)
  const [mirrorSettings, setMirrorSettings] = useState<MirrorSettings>(() => loadMirrorSettings())
  const isPointerDownRef = useRef(false)
  const paintValueRef = useRef<boolean>(true)

  const ledMode = sensorTelemetry?.flat['led.mode'] ?? 'n/a'
  const ledPattern = sensorTelemetry?.flat['led.pattern'] ?? 'n/a'
  const ledUpdated = sensorTelemetry?.flat['led.last_update_at'] ?? 'n/a'

  const ledFrame = useMemo(() => {
    const raw = sensorTelemetry?.flat['led.frame_hex']
    if (!raw) {
      return 'n/a'
    }

    return raw.length > 80 ? `${raw.slice(0, 80)}...` : raw
  }, [sensorTelemetry?.flat])

  const generatedFrameHex = useMemo(() => {
    const wireMatrix = toWireMatrix(matrix, mirrorSettings)
    return bytesToHex(matrixToBytes(wireMatrix))
  }, [matrix, mirrorSettings])

  useEffect(() => {
    const onPointerUp = () => {
      isPointerDownRef.current = false
    }

    window.addEventListener('pointerup', onPointerUp)
    return () => {
      window.removeEventListener('pointerup', onPointerUp)
    }
  }, [])

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }

    window.localStorage.setItem(LED_MIRROR_STORAGE_KEY, JSON.stringify(mirrorSettings))
  }, [mirrorSettings])

  const withBusy = async (action: () => Promise<void>) => {
    setBusy(true)
    setLastResult(null)
    try {
      await action()
      setLastResult('Команда отправлена.')
    } catch (error) {
      setLastResult(`Ошибка: ${String(error)}`)
    } finally {
      setBusy(false)
    }
  }

  const updateCell = (row: number, col: number, value: boolean) => {
    setMatrix((prev) => prev.map((line, r) => line.map((cell, c) => (r === row && c === col ? value : cell))))
  }

  const handleCellDown = (row: number, col: number) => {
    isPointerDownRef.current = true
    const nextValue = !matrix[row][col]
    paintValueRef.current = nextValue
    updateCell(row, col, nextValue)
  }

  const handleCellEnter = (row: number, col: number) => {
    if (!isPointerDownRef.current) {
      return
    }

    updateCell(row, col, paintValueRef.current)
  }

  const applyTelemetryFrameToEditor = () => {
    const raw = sensorTelemetry?.flat['led.frame_hex']
    if (!raw) {
      setLastResult('Ошибка: текущий frame из телеметрии недоступен.')
      return
    }

    const parsed = parseFrameToBytes(raw)
    if (!parsed) {
      setLastResult('Ошибка: не удалось разобрать frame из телеметрии.')
      return
    }

    const editorMatrix = fromWireMatrix(bytesToMatrix(parsed), mirrorSettings)
    setMatrix(editorMatrix)
    setCustomFrame(bytesToHex(parsed))
    setLastResult('Frame из телеметрии загружен в редактор.')
  }

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Typography variant="h6">LED панель (TM1604)</Typography>
          <Typography variant="body2" color="text.secondary">
            Здесь можно выбрать шаблон или нарисовать собственный рисунок 8x16.
          </Typography>

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Chip label={`mode: ${ledMode}`} variant="outlined" />
            <Chip label={`pattern: ${ledPattern}`} variant="outlined" />
            <Chip label={`updated: ${ledUpdated}`} variant="outlined" />
          </Stack>

          <Typography variant="body2" color="text.secondary">
            frame: {ledFrame}
          </Typography>

          <ToggleButtonGroup
            exclusive
            value={selectedPattern}
            onChange={(_, value: string | null) => {
              if (value) {
                setSelectedPattern(value)
              }
            }}
            color="primary"
            size="small"
          >
            {patterns.map((pattern) => (
              <ToggleButton key={pattern} value={pattern}>
                {pattern}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
            <Button
              variant="contained"
              disabled={busy}
              onClick={() =>
                void withBusy(async () => {
                  await onSetPattern(selectedPattern)
                })
              }
            >
              Показать pattern
            </Button>
            <Button
              variant="outlined"
              color="warning"
              disabled={busy}
              onClick={() =>
                void withBusy(async () => {
                  await onClear()
                })
              }
            >
              Очистить LED
            </Button>
          </Stack>

          <Stack spacing={1}>
            <Typography variant="subtitle2">Рисование 8x16</Typography>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={mirrorSettings.flipHorizontal}
                    onChange={(event) =>
                      setMirrorSettings((prev) => ({
                        ...prev,
                        flipHorizontal: event.target.checked,
                      }))
                    }
                  />
                }
                label="Зеркало по горизонтали"
              />
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={mirrorSettings.flipVertical}
                    onChange={(event) =>
                      setMirrorSettings((prev) => ({
                        ...prev,
                        flipVertical: event.target.checked,
                      }))
                    }
                  />
                }
                label="Зеркало по вертикали"
              />
            </Stack>
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: `repeat(${GRID_COLS}, 18px)`,
                gridTemplateRows: `repeat(${GRID_ROWS}, 18px)`,
                gap: 0.55,
                p: 1,
                borderRadius: 1.5,
                border: '1px solid rgba(130, 160, 190, 0.32)',
                width: 'fit-content',
                userSelect: 'none',
                touchAction: 'none',
              }}
            >
              {matrix.map((row, rowIndex) =>
                row.map((cell, colIndex) => (
                  <Box
                    key={`cell-${rowIndex}-${colIndex}`}
                    onPointerDown={() => handleCellDown(rowIndex, colIndex)}
                    onPointerEnter={() => handleCellEnter(rowIndex, colIndex)}
                    sx={{
                      width: 18,
                      height: 18,
                      borderRadius: 0.35,
                      backgroundColor: cell ? '#26d8a8' : '#13212f',
                      border: `1px solid ${cell ? 'rgba(35, 213, 171, 0.8)' : 'rgba(95, 123, 148, 0.35)'}`,
                      cursor: 'pointer',
                    }}
                  />
                )),
              )}
            </Box>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button variant="outlined" size="small" onClick={() => setMatrix(createEmptyMatrix())}>
                Очистить холст
              </Button>
              <Button
                variant="outlined"
                size="small"
                onClick={() => setMatrix((prev) => prev.map((line) => line.map((cell) => !cell)))}
              >
                Инвертировать
              </Button>
              <Button
                variant="outlined"
                size="small"
                onClick={() => setMatrix((prev) => prev.map((line) => line.map(() => true)))}
              >
                Залить
              </Button>
              <Button variant="outlined" size="small" onClick={applyTelemetryFrameToEditor}>
                Взять текущий frame
              </Button>
            </Stack>

            <TextField
              label="Frame из редактора"
              value={generatedFrameHex}
              fullWidth
              multiline
              minRows={2}
              InputProps={{ readOnly: true }}
              helperText="Учитывает текущие настройки зеркалирования и отправляется на LED именно в таком виде."
            />

            <Button
              variant="contained"
              disabled={busy}
              onClick={() =>
                void withBusy(async () => {
                  await onSetCustomFrame(generatedFrameHex)
                  setCustomFrame(generatedFrameHex)
                })
              }
            >
              Отправить рисунок на LED
            </Button>
          </Stack>

          <TextField
            label="Custom frame hex (16 bytes)"
            value={customFrame}
            onChange={(event) => setCustomFrame(event.target.value)}
            fullWidth
            multiline
            minRows={2}
            helperText="Пример: 00 00 38 40 40 40 3A 02 02 3A 40 40 40 38 00 00"
          />

          <Button
            variant="outlined"
            disabled={busy}
            onClick={() =>
              void withBusy(async () => {
                await onSetCustomFrame(customFrame)
              })
            }
          >
            Отправить custom frame (hex)
          </Button>

          {lastResult ? <Alert severity={lastResult.startsWith('Ошибка') ? 'error' : 'success'}>{lastResult}</Alert> : null}
        </Stack>
      </CardContent>
    </Card>
  )
}
