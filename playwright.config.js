const { defineConfig, devices } = require("@playwright/test");

const port = 5293;
const baseURL = `http://127.0.0.1:${port}`;

module.exports = defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure"
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `dotnet run --no-launch-profile --urls ${baseURL}`,
    url: `${baseURL}/api/health`,
    reuseExistingServer: false,
    timeout: 90_000,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      DatabasePath: "tests/playwright.webapp.db",
      DataProtectionKeysPath: "tests/playwright-data-protection-keys",
      BootstrapAdmin__Email: "admin-e2e@example.test",
      BootstrapAdmin__Password: "Admin!Playwright2026",
      BootstrapAdmin__FirstName: "Admin",
      BootstrapAdmin__LastName: "E2E"
    }
  }
});
