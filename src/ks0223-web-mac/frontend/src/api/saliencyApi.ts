// Client for the backend saliency proxy. Posts a raw JPEG to
// /api/saliency/frame and receives a PNG heatmap blob back. The backend
// forwards the request to the Python policy_saliency_server.py daemon.

export async function fetchSaliencyHeatmap(frameBlob: Blob, modelId: string): Promise<Blob> {
  const url = `/api/saliency/frame?modelId=${encodeURIComponent(modelId)}`
  const resp = await fetch(url, {
    method: 'POST',
    body: frameBlob,
    headers: { 'Content-Type': 'image/jpeg' },
  })
  if (!resp.ok) {
    throw new Error(`saliency proxy failed: ${resp.status}`)
  }
  return resp.blob()
}
