import { Box, Card, CardContent, Stack, Typography } from '@mui/material'
import { SaliencyOverlay } from '../components/SaliencyOverlay'
import type { RuntimeMode } from '../runtime-modes'

type Props = {
  clientInstanceId: string
  runtimeMode: RuntimeMode
  cameraAgentId?: string
  modelId?: string
}

// Live Grad-CAM/saliency view: shows which pixels drive the active
// classifier's decision. Heatmap is fetched at ~4 Hz from the backend
// proxy and overlaid on the same MJPEG stream used elsewhere in the UI.
export function ExplainabilityPage({
  clientInstanceId,
  runtimeMode,
  cameraAgentId,
  modelId = 'tl-classifier',
}: Props) {
  const params = new URLSearchParams({ clientId: clientInstanceId, runtimeMode })
  if (cameraAgentId) {
    params.set('agentId', cameraAgentId)
  }
  const mjpegUrl = `/api/camera/mjpeg?${params.toString()}`

  return (
    <Stack spacing={2.5}>
      <Card>
        <CardContent>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            Explainability — live saliency
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            Heatmap shows which pixels drive the classifier&apos;s decision. Model: <code>{modelId}</code>.
          </Typography>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'center' }}>
            <SaliencyOverlay mjpegUrl={mjpegUrl} modelId={modelId} />
          </Box>
        </CardContent>
      </Card>
    </Stack>
  )
}
