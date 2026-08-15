// Bundles the content script as a single classic (non-module) IIFE, because
// Manifest V3 `content_scripts` entries are always injected as classic
// scripts — they cannot use top-level `import`/`export` syntax the way the
// popup, options, and service-worker builds (built separately by Vite) can.
// Also copies the static manifest.json and any extension icons into dist/,
// since Vite's build (which must run before this script) only produces the
// popup/options/service-worker outputs.
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { copyFile, mkdir, readdir, stat } from 'node:fs/promises';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = resolve(__dirname, '..');
const distDir = resolve(root, 'dist');

await mkdir(distDir, { recursive: true });

await build({
  entryPoints: [resolve(root, 'src/content/index.ts')],
  bundle: true,
  format: 'iife',
  target: 'es2022',
  outfile: resolve(distDir, 'content-script.js'),
  sourcemap: false,
  logLevel: 'info'
});

await copyFile(resolve(root, 'manifest.json'), resolve(distDir, 'manifest.json'));

const iconsDir = resolve(root, 'icons');
try {
  const entries = await stat(iconsDir);
  if (entries.isDirectory()) {
    const destIcons = resolve(distDir, 'icons');
    await mkdir(destIcons, { recursive: true });
    for (const file of await readdir(iconsDir)) {
      await copyFile(resolve(iconsDir, file), resolve(destIcons, file));
    }
  }
} catch {
  // No icons directory — icons are optional for "Load unpacked".
}

console.log('content-script.js, manifest.json ready in dist/');
