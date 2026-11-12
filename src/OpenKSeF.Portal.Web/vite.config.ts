import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'initial-js-budget',
      generateBundle(_, bundle) {
        const initial = new Set<string>()
        const visit = (fileName: string) => {
          const chunk = bundle[fileName]
          if (initial.has(fileName) || chunk?.type !== 'chunk') return
          initial.add(fileName)
          chunk.imports.forEach(visit)
        }
        Object.values(bundle).forEach((chunk) => chunk.type === 'chunk' && chunk.isEntry && visit(chunk.fileName))
        const size = [...initial].reduce((sum, fileName) => sum + (bundle[fileName] as { code: string }).code.length, 0)
        if (size > 600_000) {
          this.error(`Initial JS (${[...initial].join(', ')}) is ${size} bytes, budget 600000`)
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
