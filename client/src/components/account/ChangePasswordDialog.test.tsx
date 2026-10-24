import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { ChangePasswordDialog } from "./ChangePasswordDialog";

afterEach(cleanup);

/** Fills the three fields and submits the form (bypassing the browser's own minLength check). */
function submit(current: string, next: string, repeat = next) {
  fireEvent.change(screen.getByLabelText("Current password"), { target: { value: current } });
  fireEvent.change(screen.getByLabelText("New password"), { target: { value: next } });
  fireEvent.change(screen.getByLabelText("Repeat new password"), { target: { value: repeat } });
  fireEvent.submit(screen.getByRole("button", { name: "Change password" }).closest("form")!);
}

describe("ChangePasswordDialog", () => {
  it("checks length and repetition before asking the server", async () => {
    const calls = mockFetch(() => undefined);
    renderWithProviders(<ChangePasswordDialog open onOpenChange={() => undefined} />);

    submit("old-password", "too-short");
    expect((await screen.findByRole("alert")).textContent).toContain("at least 12 characters");

    submit("old-password", "a-long-new-password", "a-different-password");
    expect((await screen.findByRole("alert")).textContent).toContain("don't match");
    expect(calls.some((c) => c.path === "/api/auth/password")).toBe(false);
  });

  it("changes the password and closes", async () => {
    const calls = mockFetch(({ method, path }) =>
      method === "POST" && path === "/api/auth/password" ? jsonResponse(null, 204) : undefined
    );
    const onOpenChange = vi.fn();
    renderWithProviders(<ChangePasswordDialog open onOpenChange={onOpenChange} />);

    submit("old-password", "a-long-new-password");

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(calls.find((c) => c.path === "/api/auth/password")?.body).toEqual({
      currentPassword: "old-password",
      newPassword: "a-long-new-password",
    });
  });

  it("shows the server's reason when the current password is wrong", async () => {
    mockFetch(({ path }) =>
      path === "/api/auth/password" ? jsonResponse({ error: "The current password is wrong", status: 400 }, 400) : undefined
    );
    const onOpenChange = vi.fn();
    renderWithProviders(<ChangePasswordDialog open onOpenChange={onOpenChange} />);

    submit("wrong-password", "a-long-new-password");

    expect((await screen.findByRole("alert")).textContent).toContain("The current password is wrong");
    expect(onOpenChange).not.toHaveBeenCalled();
  });
});
