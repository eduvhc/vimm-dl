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
      includeAssets: ['icons/icon-192.svg', 'icons/icon-512.svg', 'icons/apple-touch-icon.svg'],
      manifest: {
        name: 'Vimm/DL',
        short_name: 'Vimm/DL',
        description: 'Download queue manager with PS3 ISO conversion',
        theme_color: '#05080f',
        background_color: '#05080f',
        display: 'standalone',
        orientation: 'any',
        start_url: '/',
        icons: [
          { src: '/icons/icon-192.svg', sizes: '192x192', type: 'image/svg+xml', purpose: 'any' },
          { src: '/icons/icon-512.svg', sizes: '512x512', type: 'image/svg+xml', purpose: 'any' },
          { src: '/icons/apple-touch-icon.svg', sizes: '180x180', type: 'image/svg+xml', purpose: 'any' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,woff2}'],
        navigateFallback: '/index.html',
        runtimeCaching: [
          {
            urlPattern: /^https:\/\/fonts\.(googleapis|gstatic)\.com\/.*/i,
            handler: 'CacheFirst',
            options: { cacheName: 'google-fonts', expiration: { maxEntries: 10, maxAgeSeconds: 365 * 24 * 60 * 60 } },
          },
          {
            urlPattern: /\/api\/.*/i,
            handler: 'NetworkFirst',
            options: { cacheName: 'api', expiration: { maxEntries: 50, maxAgeSeconds: 5 * 60 } },
          },
        ],
      },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5031',
      '/hub': {
        target: 'http://localhost:5031',
        ws: true,
      },
    },
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
})
