import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Build çıxışı birbaşa Web-in wwwroot-una gedir — admin SPA /admin altında xidmət olunur.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/admin/',
  build: {
    outDir: '../src/QrCatalog.Web/wwwroot/admin',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5079',
      '/health': 'http://localhost:5079',
    },
  },
})
