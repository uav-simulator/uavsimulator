import { useEffect, useRef, useState } from 'react'
import { fetchSaliencyHeatmap } from '../api/saliencyApi'

interface Props {
  mjpegUrl: string
  modelId: string
  pollHz?: number
}

// Renders the MJPEG camera stream as the base layer and overlays a
// Grad-CAM-style heatmap on top. The heatmap is refreshed on a timer
// (default 4 Hz): each tick we draw the current MJPEG frame into a
// hidden canvas, ship the JPEG to /api/saliency/frame, and paint the
// returned PNG into a foreground canvas using multiply blending.
export function SaliencyOverlay({ mjpegUrl, modelId, pollHz = 4 }: Props) {
  const imgRef = useRef<HTMLImageElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const intervalMs = Math.max(50, Math.round(1000 / pollHz))

    const timer = window.setInterval(async () => {
      if (cancelled || !imgRef.current) {
        return
      }

      const img = imgRef.current
      if (!img.naturalWidth || !img.naturalHeight) {
        return
      }

      try {
        const tmp = document.createElement('canvas')
        tmp.width = img.naturalWidth
        tmp.height = img.naturalHeight
        const ctx = tmp.getContext('2d')
        if (!ctx) {
          throw new Error('2D context unavailable')
        }
        ctx.drawImage(img, 0, 0)

        const blob = await new Promise<Blob>((resolve, reject) =>
          tmp.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob failed'))), 'image/jpeg', 0.7),
        )
        const heatmap = await fetchSaliencyHeatmap(blob, modelId)
        if (cancelled) {
          return
        }

        const heatImg = new Image()
        const objectUrl = URL.createObjectURL(heatmap)
        heatImg.onload = () => {
          const c = canvasRef.current
          if (!c || !imgRef.current) {
            URL.revokeObjectURL(objectUrl)
            return
          }
          c.width = imgRef.current.naturalWidth
          c.height = imgRef.current.naturalHeight
          const cctx = c.getContext('2d')
          cctx?.drawImage(heatImg, 0, 0, c.width, c.height)
          URL.revokeObjectURL(objectUrl)
        }
        heatImg.onerror = () => URL.revokeObjectURL(objectUrl)
        heatImg.src = objectUrl
        setError(null)
      } catch (e: unknown) {
        const message = e instanceof Error ? e.message : String(e)
        setError(message)
      }
    }, intervalMs)

    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [mjpegUrl, modelId, pollHz])

  return (
    <div style={{ position: 'relative', display: 'inline-block' }}>
      <img
        ref={imgRef}
        src={mjpegUrl}
        alt="camera"
        crossOrigin="anonymous"
        style={{ display: 'block', maxWidth: '100%' }}
      />
      <canvas
        ref={canvasRef}
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          width: '100%',
          height: '100%',
          pointerEvents: 'none',
          mixBlendMode: 'multiply',
          opacity: 0.6,
        }}
      />
      {error && <div style={{ color: 'red', padding: 6 }}>{error}</div>}
    </div>
  )
}
