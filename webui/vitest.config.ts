import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // 单测要碰 document（词表换包要改 html@lang），jsdom 是它们的家。
    environment: 'jsdom',
  },
});
