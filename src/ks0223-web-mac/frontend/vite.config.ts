import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
          mui: ['@mui/material', '@mui/icons-material', '@emotion/react', '@emotion/styled'],
          signalr: ['@microsoft/signalr'],
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5058',
        changeOrigin: true,
      },
      '/hub': {
        target: 'http://localhost:5058',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
