import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'Receitas Mercado',
        short_name: 'Receitas',
        description: 'Controle de mantimentos e receitas da sua casa.',
        lang: 'pt-BR',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        background_color: '#faf7f2',
        theme_color: '#1f6f50',
        icons: [
          { src: 'icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any' },
          { src: 'icon-maskable.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'maskable' },
        ],
      },
      workbox: {
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health/],
        globPatterns: ['**/*.{js,css,html,svg,webmanifest}'],
        runtimeCaching: [],
      },
    }),
  ],
  build: { outDir: '../src/ReceitasMercado.Api/wwwroot', emptyOutDir: true, sourcemap: false },
  server: { port: 5173, proxy: { '/api': 'http://localhost:5083', '/health': 'http://localhost:5083' } },
  test: { environment: 'node' },
})
