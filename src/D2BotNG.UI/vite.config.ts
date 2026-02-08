import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    host: '0.0.0.0',
    port: 4200,
    allowedHosts: ['.ts.net'],
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/grpc': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      // Rendering assets (dc6, dat, etc.) served by C# server
      '/assets/rendering': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../D2BotNG/wwwroot',
    emptyOutDir: true,
  },
})
