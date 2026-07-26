import path from "node:path";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-pool-workers";
import { defineConfig } from "vitest/config";

export default defineConfig(async () => {
  const migrationsPath = path.join(__dirname, "migrations");
  const migrations = await readD1Migrations(migrationsPath);

  return {
    plugins: [
      cloudflareTest({
        wrangler: { configPath: "./wrangler.toml" },
        miniflare: {
          bindings: {
            // Test-only binding so test/apply-migrations.ts can apply the real migrations before
            // each test file runs, against the isolated per-file D1 instance vitest-pool-workers sets up.
            TEST_MIGRATIONS: migrations,
            // Secrets are normally set via `wrangler secret put`; tests provide a fixed value here
            // instead so the admin-key-gated routes are exercisable.
            ADMIN_API_KEY: "test-admin-key",
          },
        },
      }),
    ],
    test: {
      setupFiles: ["./test/apply-migrations.ts"],
    },
  };
});
