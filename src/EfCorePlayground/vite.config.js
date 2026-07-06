import { defineConfig } from 'vite';
import { viteStaticCopy } from 'vite-plugin-static-copy';
import path from 'path';

export default defineConfig({
    // Relative base so emitted asset URLs (fonts, PGlite wasm/data, chunks) resolve
    // relative to their own file location instead of the site root. This is required
    // for GitHub Pages, where the app is served from a sub-path (/ef-core-playrground/):
    // an absolute base like '/vendor/' would drop the sub-path and 404 (e.g. bootstrap
    // fonts and the PGlite FS bundle). Relative URLs work under any deployment path,
    // matching how monaco-interop.js derives its path from <base href> at runtime.
    base: './',
    build: {
        outDir: 'wwwroot/vendor',
        emptyOutDir: true,
        rollupOptions: {
            input: {
                main: path.resolve(__dirname, 'ClientApp/main.js'),
            },
            output: {
                entryFileNames: 'js/[name].js',
                chunkFileNames: 'js/[name].js',
                assetFileNames: (assetInfo) => {
                    if (assetInfo.names?.[0]?.endsWith('.css')) {
                        return 'css/[name][extname]';
                    }
                    if (assetInfo.names?.[0]?.match(/\.(woff2?|ttf|eot)$/)) {
                        return 'fonts/[name][extname]';
                    }
                    return 'assets/[name][extname]';
                },
            },
        },
    },
    plugins: [
        viteStaticCopy({
            targets: [
                {
                    src: 'node_modules/monaco-editor/min/vs/**/*',
                    dest: 'monaco/min/vs',
                },
            ],
        }),
    ],
});
