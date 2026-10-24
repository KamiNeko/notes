import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { SignInSession } from "@/lib/types";
import { SessionsDialog } from "./SessionsDialog";

afterEach(cleanup);

const now = Date.now();

function session(id: string, userAgent: string, current = false): SignInSession {
  return { id, userAgent, current, createdTime: now - 86_400_000, lastSeenTime: now - 60_000, expiresTime: now + 86_400_000 };
}

/** A server that keeps a session list and removes what the dialog ends. */
function sessionServer(initial: SignInSession[]) {
  let sessions = [...initial];
  const calls = mockFetch(({ method, path }) => {
    if (method === "GET" && path === "/api/auth/sessions") return jsonResponse(sessions);
    if (method === "DELETE" && path === "/api/auth/sessions") {
      const ended = sessions.filter((s) => !s.current).length;
      sessions = sessions.filter((s) => s.current);
      return jsonResponse({ ended });
    }
    const match = /^\/api\/auth\/sessions\/(\w+)$/.exec(path);
    if (method === "DELETE" && match) {
      sessions = sessions.filter((s) => s.id !== match[1]);
      return jsonResponse(null, 204);
    }
    return undefined;
  });
  return calls;
}

describe("SessionsDialog", () => {
  it("lists the sessions with readable device names and marks this device", async () => {
    sessionServer([
      session("a".repeat(32), "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0", true),
      session("b".repeat(32), "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Version/18.0 Mobile/15E148 Safari/604.1"),
    ]);
    renderWithProviders(<SessionsDialog open onOpenChange={() => undefined} />);

    const list = await screen.findByRole("list", { name: "Sessions" });
    const firefox = (await within(list).findByText("Firefox on Linux")).closest("li")!;
    expect(within(firefox).getByText("This device")).toBeTruthy();
    expect(within(firefox).queryByRole("button")).toBeNull();
    expect(within(list).getByRole("button", { name: "Sign out Safari on iPhone" })).toBeTruthy();
  });

  it("signs out another device", async () => {
    const calls = sessionServer([session("a".repeat(32), "Firefox/140", true), session("b".repeat(32), "curl/8.9.1")]);
    renderWithProviders(<SessionsDialog open onOpenChange={() => undefined} />);

    fireEvent.click(await screen.findByRole("button", { name: "Sign out curl/8.9.1" }));

    await waitFor(() => expect(screen.queryByText("curl/8.9.1")).toBeNull());
    expect(calls.some((c) => c.method === "DELETE" && c.path === `/api/auth/sessions/${"b".repeat(32)}`)).toBe(true);
  });

  it("signs out all other devices and is disabled when there are none", async () => {
    const calls = sessionServer([session("a".repeat(32), "Firefox/140", true), session("b".repeat(32), "One"), session("c".repeat(32), "Two")]);
    renderWithProviders(<SessionsDialog open onOpenChange={() => undefined} />);

    const button = await screen.findByRole("button", { name: "Sign out all other devices" });
    await screen.findByText("Two");
    fireEvent.click(button);

    await waitFor(() => expect(screen.queryByText("Two")).toBeNull());
    expect(calls.some((c) => c.method === "DELETE" && c.path === "/api/auth/sessions")).toBe(true);
    await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(true));
  });
});
