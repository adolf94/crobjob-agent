import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// APP_ROOT is the root build directory (base path) for the app itself, e.g.
// "/cron-agent". It is set as an env var by CI/Docker and exposed to the
// client bundle as import.meta.env.BASE_URL. Local `vite dev` always serves
// at "/" so the /api proxy and OIDC redirect stay on the root origin.
// Unset → "/" (CI always passes APP_ROOT explicitly).
export default defineConfig(({ mode }) => {
  const raw =
    mode === 'development'
      ? '/'
      : (process.env.APP_ROOT ?? process.env.BASE_URL ?? '/')
  const base = raw === '/' ? '/' : `/${raw.replace(/^\/+|\/+$/g, '')}/`

  return {
    base,
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
  }
})
