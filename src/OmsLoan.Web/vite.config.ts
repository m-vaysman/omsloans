import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],

  server: {
    // Development runs two processes: this dev server for the UI, and `dotnet run --project
    // src/OmsLoan.Api` for the API. Proxying /api through Vite means the browser only ever
    // talks to one origin, so there is no CORS configuration that exists solely for
    // Development and no relative-vs-absolute URL difference between here and Production —
    // where the API serves this build itself from the same origin. See SpaHosting.cs.
    proxy: {
      '/api': {
        target: 'http://localhost:5023',
        changeOrigin: true,
      },
    },
  },
})
