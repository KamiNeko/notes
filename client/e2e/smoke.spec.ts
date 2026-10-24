import { readFile } from "node:fs/promises";
import { expect, Page, test } from "@playwright/test";
import { DEMO_EMAIL, DEMO_PASSWORD, demoId, signIn, totpCode, uniqueWord } from "./helpers";

/** Opens the Reading List notebook and waits until its notes are listed */
async function openReadingList(page: Page) {
  await page.getByRole("complementary").first().getByRole("button", { name: /^Reading List/ }).click();
  await expect(page).toHaveURL(new RegExp(`/notebooks/${demoId("nb-reading")}(/|$)`));
  await expect(page.getByText("Book Notes: Deep Work")).toBeVisible();
}

/** Creates a note in the current list; it opens in the editor */
async function createNote(page: Page) {
  await page.getByRole("button", { name: "Create new note" }).click();
  await page.getByRole("menuitem", { name: "Note", exact: true }).click();
  await expect(page.getByLabel("Note title")).toBeVisible();
}

test.describe("sign-in", () => {
  test("rejects a wrong password and signs in with the demo account", async ({ page }) => {
    await page.goto("/notes");
    // Without a session the app sends you to the login page
    await expect(page).toHaveURL(/\/login$/);

    await page.getByLabel("Email Address").fill(DEMO_EMAIL);
    await page.getByLabel("Password").fill("wrong");
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page.getByText("Invalid email or password", { exact: false })).toBeVisible();

    await signIn(page);
    await expect(page).toHaveURL(/\/notes$/);
    await expect(page.getByRole("heading", { name: "All Notes" })).toBeVisible();
  });

  test("returns to the requested note after signing in, and signs out", async ({ page }) => {
    const roadmap = `/notebooks/${demoId("nb-projects")}/notes/${demoId("note-roadmap")}`;
    await page.goto(roadmap);
    await expect(page).toHaveURL(/\/login$/);

    await signIn(page, page.url());
    await expect(page).toHaveURL(new RegExp(`${roadmap}$`));
    await expect(page.getByRole("heading", { level: 1, name: "Website Relaunch Roadmap" })).toBeVisible();

    await page.getByRole("button", { name: "User account menu" }).click();
    await page.getByRole("menuitem", { name: "Sign Out" }).click();
    await expect(page).toHaveURL(/\/login/);
    await page.goto("/notes");
    await expect(page).toHaveURL(/\/login/);
  });
});

test("browses notebooks and tags of the demo data", async ({ page }) => {
  await signIn(page);
  const sidebar = page.getByRole("complementary").first();

  await sidebar.getByRole("button", { name: /^Recipes/ }).click();
  await expect(page).toHaveURL(new RegExp(`/notebooks/${demoId("nb-recipes")}$`));
  await expect(page.getByText("Weeknight Miso Ramen")).toBeVisible();
  await expect(page.getByText("Website Relaunch Roadmap")).toHaveCount(0);

  await sidebar.getByRole("button", { name: /^books\b/ }).click();
  await expect(page).toHaveURL(new RegExp(`/notes\\?tag=${demoId("tag-books")}$`));
  await expect(page.getByText("Book Notes: Deep Work")).toBeVisible();
  await expect(page.getByText("Books to Read in 2026")).toBeVisible();
  await expect(page.getByText("Weeknight Miso Ramen")).toHaveCount(0);
});

test("creates, edits, tags, finds, trashes and restores a note", async ({ page }) => {
  const word = uniqueWord("zebra");
  const tag = uniqueWord("tag");
  const title = `E2E note ${word}`;
  await signIn(page);
  const sidebar = page.getByRole("complementary").first();

  // Create in the Reading List notebook
  await openReadingList(page);
  await createNote(page);

  // The new note opens in the editor
  await page.getByLabel("Note title").fill(title);
  await page.locator(".cm-content").click();
  await page.keyboard.type(`Written by the smoke test: ${word}`);
  await page.getByRole("button", { name: "Done" }).click();
  await expect(page.getByRole("heading", { level: 1, name: title })).toBeVisible();
  await expect(page.locator(".prose-content").getByText(`Written by the smoke test: ${word}`)).toBeVisible();

  // Tag it
  await page.getByRole("button", { name: "Tag", exact: true }).click();
  await page.getByLabel("Add tag").fill(tag);
  await page.getByLabel("Add tag").press("Enter");
  await page.getByLabel("Add tag").press("Escape");
  await expect(page.getByRole("button", { name: `#${tag}` })).toBeVisible();
  await expect(sidebar.getByRole("button", { name: new RegExp(`^${tag}\\s*, 1 note$`) })).toBeVisible();

  // Edits survive a reload, so they reached the backend
  await page.reload();
  await expect(page.getByRole("heading", { level: 1, name: title })).toBeVisible();
  await expect(page.getByRole("button", { name: `#${tag}` })).toBeVisible();

  // Find it through search from another list
  await sidebar.getByRole("button", { name: "All Notes" }).click();
  await page.getByRole("searchbox", { name: "Search notes" }).fill(word);
  await expect(page.getByText("1 found")).toBeVisible();
  await page.getByRole("button", { name: new RegExp(title) }).click();
  await expect(page.getByRole("heading", { level: 1, name: title })).toBeVisible();
  await page.getByRole("button", { name: "Clear search" }).first().click();

  // Trash it
  await page.getByRole("button", { name: "More note actions" }).click();
  await page.getByRole("menuitem", { name: "Move to trash" }).click();
  await expect(page.getByText("Note moved to trash")).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: title })).toHaveCount(0);

  // Restore it from the trash
  await sidebar.getByRole("button", { name: "Trash" }).click();
  await page.getByText(title).first().click();
  await expect(page.getByText("In Trash")).toBeVisible();
  await page.getByRole("button", { name: "Restore" }).click();
  await expect(page.getByText("Note restored")).toBeVisible();

  await openReadingList(page);
  await expect(page.getByText(title).first()).toBeVisible();
});

test("shows a note's version history and restores an earlier version", async ({ page }) => {
  await signIn(page, `/notebooks/${demoId("nb-projects")}/notes/${demoId("note-roadmap")}`);
  await expect(page.getByRole("heading", { level: 1, name: "Website Relaunch Roadmap" })).toBeVisible();

  await page.getByRole("button", { name: "More note actions" }).click();
  await page.getByRole("menuitem", { name: "Version history" }).click();
  const history = page.getByRole("dialog", { name: "Version history" });
  await history.getByRole("button", { name: /Website relaunch/ }).first().click();
  await expect(history.getByRole("heading", { name: "Website relaunch" })).toBeVisible();
  await history.getByRole("button", { name: "Changes" }).click();
  await expect(history.getByRole("list", { name: "Line changes" }).getByText("First ideas for the relaunch:")).toBeVisible();
  await history.getByRole("button", { name: "Restore this version" }).click();

  await expect(page.getByText("Version restored")).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: "Website relaunch" })).toBeVisible();

  // The replaced text is a version now, so the restore can be undone
  await page.getByRole("button", { name: "More note actions" }).click();
  await page.getByRole("menuitem", { name: "Version history" }).click();
  await history.getByRole("button", { name: /Website Relaunch Roadmap/ }).first().click();
  await history.getByRole("button", { name: "Restore this version" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Website Relaunch Roadmap" })).toBeVisible();
});

test("attaches a file to a note", async ({ page }) => {
  const word = uniqueWord("attach");
  await signIn(page);
  await openReadingList(page);
  await createNote(page);
  await page.getByLabel("Note title").fill(`Attachment ${word}`);

  await page.locator('input[type="file"]').setInputFiles({
    name: `${word}.txt`,
    mimeType: "text/plain",
    buffer: Buffer.from("hello from the smoke test"),
  });
  // The placeholder becomes a link to the new resource
  await expect(page.locator(".cm-content")).toContainText(new RegExp(`\\[${word}\\.txt\\]\\(:/[0-9a-f]{32}\\)`));
  await page.getByRole("button", { name: "Done" }).click();

  // The rendered note links the attachment; clicking it downloads the uploaded file
  const download = page.waitForEvent("download");
  await page.locator(".prose-content").getByRole("button", { name: `${word}.txt` }).click();
  expect((await download).suggestedFilename()).toBe(`${word}.txt`);
  expect(await readFile(await (await download).path(), "utf8")).toBe("hello from the smoke test");
});

test("exports all notes as a ZIP of Markdown files", async ({ page }) => {
  await signIn(page);
  await page.getByRole("button", { name: "User account menu" }).click();
  const download = page.waitForEvent("download");
  await page.getByRole("menuitem", { name: "Export notes" }).click();
  expect((await download).suggestedFilename()).toMatch(/^notes-export-\d{4}-\d{2}-\d{2}\.zip$/);
  // File names in a ZIP's directory are stored uncompressed
  const zip = await readFile(await (await download).path());
  expect(zip.subarray(0, 2).toString()).toBe("PK");
  expect(zip.includes("Website Relaunch Roadmap.md")).toBe(true);
});

test("imports the export into a new notebook", async ({ page, baseURL }) => {
  await signIn(page);
  const zip = await (await page.request.get("/api/export")).body();

  await page.getByRole("button", { name: "User account menu" }).click();
  await page.getByRole("menuitem", { name: "Import notes" }).click();
  const dialog = page.getByRole("dialog", { name: "Import notes" });
  await dialog.getByLabel("ZIP file").setInputFiles({ name: "export.zip", mimeType: "application/zip", buffer: zip });
  await dialog.getByRole("button", { name: "Import" }).click();
  await expect(dialog.getByRole("status")).toContainText(/Imported \d+ notes/);
  await dialog.getByRole("button", { name: "Open notebook" }).click();
  await expect(page).toHaveURL(/\/notebooks\/[0-9a-f]{32}$/);
  const notebookId = page.url().split("/").pop()!;

  try {
    // The export keeps notes in their notebook folders, so they land in sub-notebooks of the import;
    // "Import …" sorts before the demo's own notebooks
    await page.getByRole("complementary").first().getByRole("button", { name: /^Projects/ }).first().click();
    await expect(page).not.toHaveURL(new RegExp(`/notebooks/(${notebookId}|${demoId("nb-projects")})$`));
    await expect(page.getByText("Website Relaunch Roadmap")).toBeVisible();
  } finally {
    // Leave the demo as it was for the other tests, even when a step above failed
    const headers = { Origin: new URL(baseURL!).origin };
    expect((await page.request.delete(`/api/notebooks/${notebookId}`, { headers })).ok()).toBe(true);
    expect((await page.request.delete(`/api/notebooks/${notebookId}?permanent=true`, { headers })).ok()).toBe(true);
  }
});

test("turns on two-factor sign-in, signs in with a recovery code and turns it off", async ({ page }) => {
  await signIn(page);
  await page.getByRole("button", { name: "User account menu" }).click();
  await page.getByRole("menuitem", { name: "Two-factor sign-in" }).click();
  await page.getByRole("button", { name: "Set up" }).click();
  await page.getByLabel("Your password").fill(DEMO_PASSWORD);
  await page.getByRole("button", { name: "Continue" }).click();
  const key = (await page.getByLabel("Setup key").textContent())!;
  await page.getByLabel("Code from the app").fill(totpCode(key));
  await page.getByRole("button", { name: "Turn on" }).click();
  const list = page.getByRole("list", { name: "Recovery codes" }).getByRole("listitem");
  await expect(list).toHaveCount(10);
  const codes = await list.allTextContents();

  try {
    await page.getByRole("button", { name: "I saved them" }).click();
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "User account menu" }).click();
    await page.getByRole("menuitem", { name: "Sign Out" }).click();
    await expect(page).toHaveURL(/\/login/);

    await page.getByLabel("Email Address").fill(DEMO_EMAIL);
    await page.getByLabel("Password").fill(DEMO_PASSWORD);
    await page.getByRole("button", { name: "Sign In" }).click();
    await page.getByLabel("Authentication code").fill(codes[0]);
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page).not.toHaveURL(/\/login/);

    await page.getByRole("button", { name: "User account menu" }).click();
    await page.getByRole("menuitem", { name: "Two-factor sign-in" }).click();
    await expect(page.getByText("9 recovery codes left.")).toBeVisible();
    await page.getByRole("button", { name: "Turn off" }).click();
    await page.getByLabel("Your password").fill(DEMO_PASSWORD);
    await page.getByLabel("Code from the app or a recovery code").fill(codes[1]);
    await page.getByRole("button", { name: "Turn off" }).click();
    await expect(page.getByRole("button", { name: "Set up" })).toBeVisible();
  } finally {
    // Leave the demo account as the other tests expect it, even when a step above failed
    const status = await page.request.get("/api/auth/2fa");
    if (status.ok() && (await status.json()).enabled) {
      await page.request.post("/api/auth/2fa/disable", {
        data: { password: DEMO_PASSWORD, code: codes[9] },
        headers: { Origin: new URL(page.url()).origin },
      });
    }
  }
});

test("navigates on a phone between menu, list and note @phone", async ({ page }) => {
  await signIn(page);

  await page.getByRole("button", { name: "Toggle navigation menu" }).click();
  const menu = page.getByRole("dialog", { name: "Navigation Menu" });
  await menu.getByRole("button", { name: /^Travel/ }).click();
  await expect(menu).toBeHidden();

  await page.getByText("Lisbon Long Weekend").click();
  await expect(page.getByRole("heading", { level: 1, name: "Lisbon Long Weekend" })).toBeVisible();
  // One pane at a time: the list is hidden while the note is open
  await expect(page.getByRole("textbox", { name: "Filter this list" })).toBeHidden();

  await page.getByRole("button", { name: "Close note view" }).click();
  await expect(page.getByRole("textbox", { name: "Filter this list" })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/notebooks/${demoId("nb-travel")}$`));
});
