import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Default matches Api "http" launch profile (see OpenSignature.Api Properties/launchSettings.json).
// Override with VITE_PROXY_TARGET=https://localhost:7010 if using the https profile.
const apiTarget = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5270'

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
