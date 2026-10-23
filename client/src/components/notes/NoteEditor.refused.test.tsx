import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { Note } from "@/lib/types";
import { resetConnection } from "@/lib/connection";
import { draftOwner, loadDraft, setDraftOwner } from "@/lib/drafts";
import { FetchCall, jsonResponse, mockFetch } from "@/test/fetch-mock";
import { NoteEditor } from "./NoteEditor";

// CodeMirror needs layout APIs jsdom lacks; a textarea stands in for it
vi.mock("./MarkdownEditor", async () => {
  const { forwardRef } = await import("react");
  return {
    UPLOAD_PLACEHOLDER: /$^/g,
    MarkdownEditor: forwardRef<unknown, { value: string; onChange: (v: string) => void }>(function MarkdownEditor(
      { value, onChange },
      ref
    ) {
      void ref;
      return <textarea aria-label="Note body" value={value} onChange={(e) => onChange(e.target.value)} />;
    }),
  };
});
vi.mock("@/hooks/use-mobile", () => ({ useIsMobile: () => false }));

const NOTE_ID = "0123456789abcdef0123456789abcdef";
const note: Note = {
  id: NOTE_ID,
  title: "Shopping",
  body: "Milk",
  updatedTime: 1000,
  createdTime: 500,
  parentId: "f".repeat(32),
  notebookId: "f".repeat(32),
  notebookTitle: "Personal",
  tags: [],
  isTodo: false,
  todoCompleted: false,
};
const REFUSAL = `Note not found: ${NOTE_ID}`;

let calls: FetchCall[];

beforeEach(() => {
  localStorage.clear();
  resetConnection();
  setDraftOwner(draftOwner("me@example.com", "user-1"));
  calls = mockFetch(({ method, path }) => {
    if (method === "PUT" && path === `/api/notes/${NOTE_ID}`) return jsonResponse({ error: REFUSAL, status: 404 }, 404);
    return undefined;
  });
});

afterEach(cleanup);

describe("NoteEditor save refused for good (e.g. 404)", () => {
  it("shows the server's reason in the editor and keeps the text without retrying", async () => {
    const queryClient = new QueryClient();
    const onDone = vi.fn();
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <NoteEditor note={note} onDone={onDone} />
        </MemoryRouter>
      </QueryClientProvider>
    );

    fireEvent.change(screen.getByLabelText("Note body"), { target: { value: "Milk\nBread" } });
    fireEvent.click(screen.getByRole("button", { name: "Done" }));

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain(REFUSAL);
    expect(alert.textContent).toContain("Not saved.");
    expect(onDone).not.toHaveBeenCalled();
    expect(loadDraft(NOTE_ID)?.body).toBe("Milk\nBread");
    // A refusal is not a connection problem: no automatic retries
    expect(calls.filter((c) => c.method === "PUT")).toHaveLength(1);
  });
});
