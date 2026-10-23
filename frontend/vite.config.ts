import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// 构建产物进 dist，Web 宿主通过 LocalPaths.FindFrontendDist() 自动找到它。
// 开发期把 /api 与 SSE 代理到本机宿主（默认 127.0.0.1:5180）。
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5180',
        changeOrigin: false,
      },
    },
  },
});
