import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  // 用系统自带的 Edge：免下载浏览器的依赖，CI 的 windows-latest 也自带。
  use: { channel: 'msedge' },
  webServer: {
    command: 'npm run preview -- --port 4173 --strictPort',
    port: 4173,
    reuseExistingServer: true,
  },
});
