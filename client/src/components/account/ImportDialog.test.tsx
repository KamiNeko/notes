import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { LocationProbe, renderWithProviders } from "@/test/render";
import { ImportDialog } from "./ImportDialog";

afterEach(cleanup);

const zip = () => new File([new Uint8Array([0x50, 0x4b, 3, 4])], "export.zip", { type: "application/zip" });

describe("ImportDialog", () => {
  it("uploads the ZIP, reports what was imported and opens the new notebook", async () => {
    const calls = mockFetch(({ method, path }) =>
      method === "POST" && path === "/api/import"
        ? jsonResponse(
            {
              notebookId: "a".repeat(32),
              notebookTitle: "Import 2026-10-24 09:30",
              notes: 12,
              notebooks: 3,
              attachments: 1,
              skipped: [{ path: "../evil.md", reason: "unsafe path" }],
              skippedCount: 1,
            },
            201
          )
        : undefined
    );
    renderWithProviders(
      <>
        <ImportDialog open onOpenChange={() => undefined} />
        <LocationProbe />
      </>
    );

    expect((screen.getByRole("button", { name: "Import" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByLabelText("ZIP file"), { target: { files: [zip()] } });
    fireEvent.click(screen.getByRole("button", { name: "Import" }));

    const status = await screen.findByRole("status");
    expect(status.textContent).toContain("Imported 12 notes, 3 notebooks and 1 attachment into Import 2026-10-24 09:30.");
    expect(within(screen.getByRole("list", { name: "Left out" })).getByText("../evil.md")).toBeTruthy();
    const upload = calls.find((c) => c.path === "/api/import");
    expect(upload?.method).toBe("POST");

    fireEvent.click(screen.getByRole("button", { name: "Open notebook" }));
    expect(screen.getByTestId("location").textContent).toBe(`/notebooks/${"a".repeat(32)}`);
  });

  it("shows the server's reason when the import fails", async () => {
    mockFetch(({ path }) => (path === "/api/import" ? jsonResponse({ error: "This is not a ZIP file", status: 400 }, 400) : undefined));
    renderWithProviders(<ImportDialog open onOpenChange={() => undefined} />);

    fireEvent.change(screen.getByLabelText("ZIP file"), { target: { files: [zip()] } });
    fireEvent.click(screen.getByRole("button", { name: "Import" }));

    expect((await screen.findByRole("alert")).textContent).toBe("This is not a ZIP file");
  });
});
