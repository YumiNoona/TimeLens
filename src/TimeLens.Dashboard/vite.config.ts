import { defineConfig } from 'vite';
import { svelte, vitePreprocess } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte({ preprocess: vitePreprocess() })],
  define: {
    __DEV__: 'false',
    __APP_VERSION__: JSON.stringify(process.env.npm_package_version ?? '0.0.0'),
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:47821',
        changeOrigin: true,
      },
    },
  },
});
