import { applyD1Migrations, env } from "cloudflare:test";

// Setup files run outside per-test-file storage isolation and may run multiple times;
// applyD1Migrations() only applies migrations that haven't already been applied, so this is safe.
// `TEST_MIGRATIONS` is a test-only binding (see vitest.config.ts) with no production Env type, hence the cast.
const testEnv = env as unknown as { DB: D1Database; TEST_MIGRATIONS: Parameters<typeof applyD1Migrations>[1] };
await applyD1Migrations(testEnv.DB, testEnv.TEST_MIGRATIONS);
