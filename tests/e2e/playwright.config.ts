import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './specs',
  timeout: 30_000,
  retries: process.env.CI ? 1 : 0,
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5079',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
})
