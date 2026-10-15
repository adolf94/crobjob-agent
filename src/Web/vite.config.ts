import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    proxy: {
      '/api': 'http://localhost:5147',
    },
  },
  build: {
    outDir: '../Api/wwwroot',
    emptyOutDir: true,
  },
})
