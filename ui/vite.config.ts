import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  base: './', // Critical for WebView2 / file / local virtual host mapping
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    minify: 'esbuild',
    target: 'esnext',
    // The UI is served from the app's own virtual host, mapped to files on disk, so chunk size is
    // not a transfer cost here. React, Prism, icons and xterm are already split for caching; what is
    // left is application code, and 500 kB is a web-download heuristic that does not apply.
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks: {
          // The entry points are listed explicitly: apps import `react/jsx-runtime` and
          // `react-dom/client`, and without them Rollup folded React into the main chunk, which
          // left a 4.8 kB "vendor" file beside a 750 kB index.
          vendor: ['react', 'react-dom', 'react/jsx-runtime', 'react-dom/client', 'zustand'],
          xterm: ['@xterm/xterm', '@xterm/addon-fit', '@xterm/addon-web-links'],
          icons: ['lucide-react'],
          // Prism plus its language packs is the single largest non-React dependency.
          prism: ['prismjs'],
        },
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]',
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
  },
});
