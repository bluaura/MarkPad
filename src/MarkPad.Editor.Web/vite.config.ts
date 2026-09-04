import { defineConfig } from 'vite'
import { fileURLToPath } from 'node:url'
import { readFileSync } from 'node:fs'

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as { version: string }

// ARCHITECTURE.md §8.2: single bundle emitted straight into the WinUI app's Assets/editor.
// Mermaid is the only dynamically imported chunk (loaded on first mermaid code block).
export default defineConfig({
  base: './',
  define: {
    __EDITOR_VERSION__: JSON.stringify(pkg.version),
  },
  build: {
    outDir: fileURLToPath(new URL('../MarkPad.App/Assets/editor', import.meta.url)),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    chunkSizeWarningLimit: 3000,
    rollupOptions: {
      output: {
        // Keep everything except mermaid in one chunk so the WebView loads a single script.
        manualChunks(id) {
          if (id.includes('node_modules/mermaid') || id.includes('node_modules/@mermaid')) return 'mermaid'
          return undefined
        },
        entryFileNames: 'editor.js',
        chunkFileNames: '[name].js',
        assetFileNames: '[name][extname]',
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    include: ['test/**/*.spec.ts'],
  },
})
