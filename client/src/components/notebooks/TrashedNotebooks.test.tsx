import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { TrashedNotebook } from "@/lib/types";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { TrashedNotebooks } from "./TrashedNotebooks";

const recipes: TrashedNotebook = {
  id: "a".repeat(32),
  title: "Recipes",
  parentId: "",
  deletedTime: 1,
  noteCount: 3,
  notebookCount: 1,
  earlierNoteCount: 2,
};
const work: TrashedNotebook = { ...recipes, id: "b".repeat(32), title: "Work", noteCount: 0, notebookCount: 0, earlierNoteCount: 0 };

afterEach(cleanup);

/** Serves the trashed notebooks; restore and purge take a notebook out of the list. */
function serveTrash(initial: TrashedNotebook[]) {
  let trash = initial;
  return mockFetch(({ method, path }) => {
    if (path === "/api/notebooks/trash") return jsonResponse(trash);
    const restore = /^\/api\/notebooks\/([0-9a-f]{32})\/restore$/.exec(path);
    if (method === "POST" && restore) {
      trash = trash.filter((nb) => nb.id !== restore[1]);
      return jsonResponse({ notebooks: 2, notes: 3 });
    }
    const purge = /^\/api\/notebooks\/([0-9a-f]{32})\?permanent=true$/.exec(path);
    if (method === "DELETE" && purge) {
      trash = trash.filter((nb) => nb.id !== purge[1]);
      return jsonResponse({ notebooks: 2, notes: 5 });
    }
    return undefined;
  });
}

describe("TrashedNotebooks", () => {
  it("lists trashed notebooks with what a restore brings back, filtered by the note filter", async () => {
    serveTrash([recipes, work]);
    const { rerender } = renderWithProviders(<TrashedNotebooks notesHeading />);

    expect(await screen.findByText("Recipes")).toBeTruthy();
    expect(screen.getByText("3 notes, 1 sub-notebook")).toBeTruthy();
    expect(screen.getByText("empty")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Notes" })).toBeTruthy();

    rerender(<TrashedNotebooks filterText="rec" />);
    expect(screen.getByText("Recipes")).toBeTruthy();
    expect(screen.queryByText("Work")).toBeNull();
  });

  it("restores a notebook", async () => {
    const calls = serveTrash([recipes]);
    renderWithProviders(<TrashedNotebooks />);

    fireEvent.click(await screen.findByRole("button", { name: "Restore Recipes" }));

    await waitFor(() => expect(screen.queryByText("Recipes")).toBeNull());
    expect(calls.some((c) => c.method === "POST" && c.path === `/api/notebooks/${recipes.id}/restore`)).toBe(true);
  });

  it("deletes a notebook permanently only after confirming, naming notes trashed earlier", async () => {
    const calls = serveTrash([recipes]);
    renderWithProviders(<TrashedNotebooks />);

    fireEvent.click(await screen.findByRole("button", { name: "Delete Recipes permanently" }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog.textContent).toContain("plus 2 notes trashed earlier");
    expect(calls.some((c) => c.method === "DELETE")).toBe(false);

    fireEvent.click(within(dialog).getByRole("button", { name: "Delete permanently" }));

    await waitFor(() => expect(screen.queryByText("Recipes")).toBeNull());
    expect(calls.some((c) => c.method === "DELETE" && c.path === `/api/notebooks/${recipes.id}?permanent=true`)).toBe(true);
  });

  it("renders nothing when the trash has no notebooks", async () => {
    const calls = serveTrash([]);
    const { container } = renderWithProviders(<TrashedNotebooks notesHeading />);
    await waitFor(() => expect(calls.some((c) => c.path === "/api/notebooks/trash")).toBe(true));
    await waitFor(() => expect(container.textContent).toBe(""));
  });
});
