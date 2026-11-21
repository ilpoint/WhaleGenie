import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // 单测要碰 document（词表换包要改 html@lang），jsdom 是它们的家；e2e/ 是 Playwright 的
    // 地盘，它的 beforeEach 不归 vitest 收。
    environment: 'jsdom',
    exclude: ['**/node_modules/**', '**/dist/**', 'e2e/**'],
  },
});
