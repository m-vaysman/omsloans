import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],

  server: {
    // Development: Vite for the UI, `dotnet run --project src/OmsLoan.Api` for the API.
    // Proxy /api so the browser sees one origin — no Dev-only CORS, no URL-shape difference
    // from Production where the API serves this build same-origin. See SpaHosting.cs.
    proxy: {
      '/api': {
        target: 'http://localhost:5023',
        changeOrigin: true,
      },
    },
  },
})
