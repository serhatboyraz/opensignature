import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Api https profile: https://localhost:7010 (see OpenSignature.Api Properties/launchSettings.json)
const apiTarget = process.env.VITE_PROXY_TARGET ?? 'https://localhost:7010'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
