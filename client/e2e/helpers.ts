import { createHash, createHmac } from "node:crypto";
import { expect, Page } from "@playwright/test";

/** Demo account from backend/src/Notes.Api/Data/DemoSeeder.cs */
export const DEMO_EMAIL = "demo@notes.test";
export const DEMO_PASSWORD = "demo-password";

/** Same stable ids as DemoSeeder.DemoId */
export const demoId = (key: string) => createHash("md5").update(`notes-demo:${key}`).digest("hex");

/** Signs in with the demo account and waits for the notes page. */
export async function signIn(page: Page, path = "/login") {
  await page.goto(path);
  await page.getByLabel("Email Address").fill(DEMO_EMAIL);
  await page.getByLabel("Password").fill(DEMO_PASSWORD);
  await page.getByRole("button", { name: "Sign In" }).click();
  await expect(page).not.toHaveURL(/\/login/);
}

/** A word no seeded note contains, so searches and lists find only what the test created. */
export function uniqueWord(prefix: string) {
  return `${prefix}${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
}

/** The current code of an authenticator app for a Base32 setup key (RFC 6238: SHA-1, 30 s, 6 digits). */
export function totpCode(key: string, time = Date.now()) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of key.replace(/\s/g, "")) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const secret = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(time / 30_000)));
  const hash = createHmac("sha1", secret).update(counter).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  return ((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
}
