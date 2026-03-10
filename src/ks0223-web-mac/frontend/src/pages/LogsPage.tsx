import { LogPanel } from '../components/LogPanel'
import type { LogFileInfo, StatusDto } from '../types'

type Props = {
  status: StatusDto | null
  files: LogFileInfo[]
  onStart: (tag: string) => Promise<void>
  onStop: () => Promise<void>
  onOpenFolder: () => Promise<void>
  onRefresh: () => Promise<void>
}

export function LogsPage({ status, files, onStart, onStop, onOpenFolder, onRefresh }: Props) {
  return (
    <LogPanel
      status={status}
      files={files}
      onStart={onStart}
      onStop={onStop}
      onOpenFolder={onOpenFolder}
      onRefresh={onRefresh}
    />
  )
}
