import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'entry-chunk-budget',
      generateBundle(_, bundle) {
        for (const chunk of Object.values(bundle)) {
          if (chunk.type === 'chunk' && chunk.isEntry && chunk.code.length > 500_000) {
            this.error(`Entry chunk ${chunk.fileName} is ${chunk.code.length} bytes, budget 500000`)
          }
        }
      },
    },
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'build',
    // pdfmake, its fonts and the KSeF converter load lazily on PDF export and cannot be split further.
    chunkSizeWarningLimit: 2600,
    rollupOptions: {
      output: {
        manualChunks: (id) => (/node_modules\/(react|react-dom|react-router|scheduler)\//.test(id) ? 'react' : undefined),
      },
    },
  },
})
