import { createHash } from "node:crypto";
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
