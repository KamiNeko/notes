import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { UserInfo } from "@/lib/types";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { UsersDialog } from "./UsersDialog";

const me: UserInfo = { id: "a".repeat(32), email: "admin@example.com", displayName: "Admin", isAdmin: true, createdTime: 0 };
const other: UserInfo = { id: "b".repeat(32), email: "friend@example.com", displayName: "", isAdmin: false, createdTime: 0 };

vi.mock("@/hooks/use-auth", () => ({ useAuth: () => ({ user: me, email: me.email }) }));

afterEach(cleanup);

describe("UsersDialog", () => {
  it("lists the accounts and adds a user", async () => {
    let users = [me, other];
    const calls = mockFetch(({ method, path, body }) => {
      if (path === "/api/admin/users" && method === "GET") return jsonResponse(users);
      if (path === "/api/admin/users" && method === "POST") {
        const created = { ...other, id: "c".repeat(32), email: (body as { email: string }).email };
        users = [...users, created];
        return jsonResponse(created, 201);
      }
      return undefined;
    });
    renderWithProviders(<UsersDialog open onOpenChange={() => undefined} />);

    const list = await screen.findByRole("list", { name: "Accounts" });
    expect(await within(list).findByText("friend@example.com")).toBeTruthy();
    expect(within(list).getByText("You")).toBeTruthy();

    const form = screen.getByRole("form", { name: "Add user" });
    fireEvent.change(within(form).getByLabelText("Email"), { target: { value: " new@example.com " } });
    fireEvent.change(within(form).getByLabelText("Password"), { target: { value: "short" } });
    fireEvent.click(within(form).getByRole("button", { name: "Add user" }));
    expect(await within(form).findByRole("alert")).toBeTruthy();
    expect(calls.some((c) => c.method === "POST")).toBe(false);

    fireEvent.change(within(form).getByLabelText("Password"), { target: { value: "long-enough-password" } });
    fireEvent.click(within(form).getByRole("button", { name: "Add user" }));
    expect(await within(list).findByText("new@example.com")).toBeTruthy();
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({
      email: "new@example.com",
      password: "long-enough-password",
      isAdmin: false,
    });
  });

  it("shows the server's reason when an account can't be added", async () => {
    mockFetch(({ method, path }) => {
      if (path === "/api/admin/users" && method === "GET") return jsonResponse([me]);
      if (method === "POST") return jsonResponse({ error: "A user with this email already exists" }, 409);
      return undefined;
    });
    renderWithProviders(<UsersDialog open onOpenChange={() => undefined} />);
    const form = screen.getByRole("form", { name: "Add user" });
    fireEvent.change(within(form).getByLabelText("Email"), { target: { value: "admin@example.com" } });
    fireEvent.change(within(form).getByLabelText("Password"), { target: { value: "long-enough-password" } });
    fireEvent.click(within(form).getByRole("button", { name: "Add user" }));
    await waitFor(() => expect(within(form).getByRole("alert").textContent).toContain("already exists"));
  });
});
