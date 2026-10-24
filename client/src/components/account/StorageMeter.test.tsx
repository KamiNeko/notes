import { afterEach, describe, expect, it } from "vitest";
import { cleanup, screen } from "@testing-library/react";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { StorageMeter } from "./StorageMeter";

afterEach(cleanup);

describe("StorageMeter", () => {
  it("shows the used space against the limit", async () => {
    mockFetch(({ path }) => (path === "/api/storage" ? jsonResponse({ usedBytes: 95 * 1024 * 1024, quotaBytes: 100 * 1024 * 1024 }) : undefined));
    renderWithProviders(<StorageMeter enabled />);

    expect(await screen.findByText("Attachments: 95 MB of 100 MB used")).toBeTruthy();
    expect(screen.getByRole("progressbar", { name: "Storage used" }).getAttribute("aria-valuenow")).toBe("95");
  });

  it("shows only the used space without a limit", async () => {
    mockFetch(({ path }) => (path === "/api/storage" ? jsonResponse({ usedBytes: 2048, quotaBytes: null }) : undefined));
    renderWithProviders(<StorageMeter enabled />);

    expect(await screen.findByText("Attachments: 2 KB used")).toBeTruthy();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("fetches nothing while the menu is closed", () => {
    const calls = mockFetch(() => undefined);
    renderWithProviders(<StorageMeter enabled={false} />);
    expect(calls).toHaveLength(0);
  });
});
