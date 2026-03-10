import FolderOpenIcon from '@mui/icons-material/FolderOpen'
import RefreshIcon from '@mui/icons-material/Refresh'
import {
  Box,
  Button,
  Card,
  CardContent,
  List,
  ListItem,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { LogFileInfo, StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  files: LogFileInfo[]
  onStart: (tag: string) => Promise<void>
  onStop: () => Promise<void>
  onOpenFolder: () => Promise<void>
  onRefresh: () => Promise<void>
}

export function LogPanel({ status, files, onStart, onStop, onOpenFolder, onRefresh }: Props) {
  const [tag, setTag] = useState('')

  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Typography variant="h6">Логи</Typography>

          <TextField
            label="Тег сессии"
            value={tag}
            onChange={(event) => setTag(event.target.value)}
            size="small"
            helperText="Опционально: например test_run_1"
          />

          <Stack direction="row" spacing={1}>
            <Button
              variant="contained"
              onClick={() => void onStart(tag)}
              disabled={status?.isLogging ?? false}
            >
              Старт логирования
            </Button>
            <Button
              variant="outlined"
              color="warning"
              onClick={() => void onStop()}
              disabled={!(status?.isLogging ?? false)}
            >
              Стоп логирования
            </Button>
            <Button variant="outlined" startIcon={<FolderOpenIcon />} onClick={() => void onOpenFolder()}>
              Открыть папку
            </Button>
            <Button variant="text" startIcon={<RefreshIcon />} onClick={() => void onRefresh()}>
              Обновить
            </Button>
          </Stack>

          <Typography variant="body2" color="text.secondary">
            Активный файл: {status?.currentLogFile ?? 'нет'}
          </Typography>

          <Box>
            <Typography variant="subtitle2">Последние файлы</Typography>
            <List dense>
              {files.map((file) => (
                <ListItem key={file.absolutePath} disablePadding>
                  <ListItemText
                    primary={file.name}
                    secondary={`${(file.sizeBytes / 1024).toFixed(1)} KB • ${new Date(file.lastWriteTimeUtc).toLocaleString()}`}
                  />
                </ListItem>
              ))}
            </List>
          </Box>
        </Stack>
      </CardContent>
    </Card>
  )
}
