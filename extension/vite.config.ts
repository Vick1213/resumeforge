import { defineConfig } from 'vite';
import { resolve } from 'node:path';

// Content script and background service worker have different execution
// contexts than the extension pages, so this config only builds the popup,
// options page, and the background service worker. The content script is
// bundled separately (see scripts/build-content.mjs) because MV3 content
// scripts declared via manifest.json `content_scripts` must be classic
// scripts, not ES modules, while the popup/options pages and the service
// worker are perfectly happy with Vite's default ES module output.
export default defineConfig({
  root: resolve(__dirname, 'src'),
  publicDir: false,
  build: {
    outDir: resolve(__dirname, 'dist'),
    emptyOutDir: true,
    target: 'es2022',
    rollupOptions: {
      input: {
        popup: resolve(__dirname, 'src/popup/index.html'),
        options: resolve(__dirname, 'src/options/index.html'),
        'service-worker': resolve(__dirname, 'src/background/service-worker.ts')
      },
      output: {
        entryFileNames: (chunk) =>
          chunk.name === 'service-worker' ? 'service-worker.js' : 'js/[name].js',
        chunkFileNames: 'js/[name]-[hash].js',
        assetFileNames: 'assets/[name][extname]'
      }
    }
  }
});
