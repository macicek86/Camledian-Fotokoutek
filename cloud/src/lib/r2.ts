import { AwsClient } from "aws4fetch";
import type { Env } from "../types";

/**
 * Presigned R2 (S3-compatible) PUT URL so the Windows app can upload a photo directly to storage
 * instead of streaming the whole file through the Worker's own memory (spec §34: "necpěte zbytečně
 * velká data přes omezenou Worker paměť, pokud lze použít efektivnější cestu").
 *
 * Requires the R2 API token secrets (R2_ACCOUNT_ID / R2_ACCESS_KEY_ID / R2_SECRET_ACCESS_KEY) to be
 * configured via `wrangler secret put` — see .env.example. The caller must PUT with the *same*
 * Content-Type used here, since it's part of what got signed.
 */
export async function createPresignedUploadUrl(
  env: Env,
  bucketName: string,
  key: string,
  contentType: string,
  expiresInSeconds = 900,
): Promise<string> {
  if (!env.R2_ACCOUNT_ID || !env.R2_ACCESS_KEY_ID || !env.R2_SECRET_ACCESS_KEY) {
    throw new Error(
      "R2 S3 API credentials are not configured (R2_ACCOUNT_ID / R2_ACCESS_KEY_ID / R2_SECRET_ACCESS_KEY).",
    );
  }

  const aws = new AwsClient({
    accessKeyId: env.R2_ACCESS_KEY_ID,
    secretAccessKey: env.R2_SECRET_ACCESS_KEY,
    service: "s3",
    region: "auto",
  });

  const url = new URL(`https://${env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com/${bucketName}/${encodeURIComponent(key)}`);
  // X-Amz-Expires must be present in the query string *before* signing — SigV4 presigned URLs sign
  // over it rather than accepting it as a separate parameter.
  url.searchParams.set("X-Amz-Expires", String(expiresInSeconds));

  const signedRequest = await aws.sign(url.toString(), {
    method: "PUT",
    headers: { "content-type": contentType },
    aws: { signQuery: true },
  });

  return signedRequest.url;
}
