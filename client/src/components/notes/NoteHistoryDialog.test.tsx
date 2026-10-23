import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { Note } from "@/lib/types";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { NoteHistoryDialog } from "./NoteHistoryDialog";

const NOTE_ID = "0123456789abcdef0123456789abcdef";
const VERSION_ID = "fedcba9876543210fedcba9876543210";

const note: Note = {
  id: NOTE_ID,
  title: "Packing list",
  body: "Current text",
  updatedTime: 5000,
  createdTime: 1000,
  parentId: "f".repeat(32),
  notebookId: "f".repeat(32),
  notebookTitle: "Travel",
  tags: [],
  isTodo: false,
  todoCompleted: false,
};

const version = { id: VERSION_ID, noteId: NOTE_ID, title: "Packing list", updatedTime: 3000, createdTime: 4000 };

function mockHistory(restoreStatus = 200) {
  return mockFetch(({ method, path }) => {
    if (path === `/api/notes/${NOTE_ID}/versions`) return jsonResponse([{ ...version, preview: "Old text" }]);
    if (path === `/api/notes/${NOTE_ID}/versions/${VERSION_ID}`) return jsonResponse({ ...version, body: "Old **text**" });
    if (method === "POST" && path === `/api/notes/${NOTE_ID}/versions/${VERSION_ID}/restore`) {
      return restoreStatus === 200
        ? jsonResponse({ ...note, body: "Old **text**", updatedTime: 6000 })
        : jsonResponse({ error: "Conflict" }, restoreStatus);
    }
    return undefined;
  });
}

afterEach(cleanup);

describe("NoteHistoryDialog", () => {
  it("lists versions, previews the selected one and restores it based on the current note", async () => {
    const calls = mockHistory();
    let open = true;
    renderWithProviders(<NoteHistoryDialog note={note} open onOpenChange={(value) => (open = value)} />);

    fireEvent.click(await screen.findByText("Old text"));
    expect(await screen.findByText("text", { selector: "strong" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Restore this version" }));
    await waitFor(() => expect(open).toBe(false));
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({ baseUpdatedTime: 5000 });
  });

  it("says when there are no earlier versions", async () => {
    mockFetch(({ path }) => (path.endsWith("/versions") ? jsonResponse([]) : undefined));
    renderWithProviders(<NoteHistoryDialog note={note} open onOpenChange={() => undefined} />);
    expect(await screen.findByText(/No earlier versions yet/)).toBeTruthy();
  });

  it("closes on a conflict instead of overwriting a newer change", async () => {
    mockHistory(409);
    let open = true;
    renderWithProviders(<NoteHistoryDialog note={note} open onOpenChange={(value) => (open = value)} />);
    fireEvent.click(await screen.findByText("Old text"));
    await screen.findByText("text", { selector: "strong" });
    fireEvent.click(screen.getByRole("button", { name: "Restore this version" }));
    await waitFor(() => expect(open).toBe(false));
  });
});
