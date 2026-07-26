const URL_SAFE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

/** Long, unguessable token for photo download URLs (spec §40) — mirrors
 * Camledian.Photobooth.Core.Utilities.TokenGenerator on the Windows side so both halves of the
 * system produce tokens with the same entropy characteristics. */
export function createDownloadToken(length = 32): string {
  return randomFromAlphabet(URL_SAFE_ALPHABET, length);
}

export function createId(): string {
  return crypto.randomUUID();
}

function randomFromAlphabet(alphabet: string, length: number): string {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  let result = "";
  for (let i = 0; i < length; i++) {
    result += alphabet[bytes[i]! % alphabet.length];
  }
  return result;
}

/** SHA-256 hex digest — device tokens are stored hashed, never in plaintext (spec §36: "Token
 * bezpečně ulož"). */
export async function sha256Hex(input: string): Promise<string> {
  const data = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}
