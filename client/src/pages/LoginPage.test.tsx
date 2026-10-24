import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { AuthProvider } from "@/hooks/use-auth";
import { getStoredEmail } from "@/lib/api";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { LocationProbe, renderWithProviders } from "@/test/render";
import { LoginPage } from "./LoginPage";

/** Backend stub: no session until a login with demo/demo succeeds */
function mockBackend() {
  let signedIn = false;
  return mockFetch(({ method, path, body }) => {
    if (path === "/api/auth/check") {
      return signedIn ? jsonResponse({ user: { id: "u1", email: "demo@example.com", displayName: "", isAdmin: false, createdTime: 0 } }) : jsonResponse({ error: "No session" }, 401);
    }
    if (method === "POST" && path === "/api/auth/login") {
      const { email, password, code } = body as { email: string; password: string; code?: string };
      // An account with two-factor sign-in: the code comes in a second request
      if (email === "tfa@example.com" && password === "demo") {
        if (code === "123456") {
          signedIn = true;
          return jsonResponse({ userId: "u2", user: { id: "u2", email, displayName: "", isAdmin: false, createdTime: 0, twoFactor: true } });
        }
        return jsonResponse(
          { error: code ? "The code is wrong or was already used" : "Enter the code from your authenticator app", status: 401, details: { secondFactor: true } },
          401
        );
      }
      if (email === "demo@example.com" && password === "demo") {
        signedIn = true;
        return jsonResponse({ userId: "u1", ...{ user: { id: "u1", email: "demo@example.com", displayName: "", isAdmin: false, createdTime: 0 } } });
      }
      return jsonResponse({ error: "Invalid email or password" }, 401);
    }
    return undefined;
  });
}

function renderLogin(route: string | { pathname: string; state?: unknown } = "/login") {
  return renderWithProviders(
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<p>Signed-in area</p>} />
      </Routes>
      <LocationProbe />
    </AuthProvider>,
    { route }
  );
}

function signIn(email: string, password: string) {
  fireEvent.change(screen.getByLabelText("Email Address"), { target: { value: email } });
  fireEvent.change(screen.getByLabelText("Password"), { target: { value: password } });
  fireEvent.click(screen.getByRole("button", { name: "Sign In" }));
}

beforeEach(() => localStorage.clear());
afterEach(cleanup);

describe("LoginPage", () => {
  it("asks for the missing field without calling the server", async () => {
    const calls = mockBackend();
    renderLogin();
    await waitFor(() => expect(calls.some((c) => c.path === "/api/auth/check")).toBe(true));

    fireEvent.submit(screen.getByRole("button", { name: "Sign In" }).closest("form")!);
    expect(await screen.findByText("Please enter your email address.")).toBeTruthy();

    fireEvent.change(screen.getByLabelText("Email Address"), { target: { value: "demo@example.com" } });
    fireEvent.submit(screen.getByRole("button", { name: "Sign In" }).closest("form")!);
    expect(await screen.findByText("Please enter your password.")).toBeTruthy();

    expect(calls.some((c) => c.path === "/api/auth/login")).toBe(false);
  });

  it("shows the server's error for wrong credentials and stays on the page", async () => {
    mockBackend();
    renderLogin();

    signIn("demo@example.com", "wrong");

    expect(await screen.findByText("Invalid email or password")).toBeTruthy();
    expect(screen.getByTestId("location").textContent).toBe("/login");
    expect((screen.getByRole("button", { name: "Sign In" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("signs in with a trimmed email and opens the notes", async () => {
    const calls = mockBackend();
    renderLogin();

    signIn("  demo@example.com ", "demo");

    expect(await screen.findByText("Signed-in area")).toBeTruthy();
    expect(screen.getByTestId("location").textContent).toBe("/notes");
    expect(calls.find((c) => c.path === "/api/auth/login")?.body).toEqual({ email: "demo@example.com", password: "demo" });
    expect(getStoredEmail()).toBe("demo@example.com");
  });

  it("returns to the page the user came from", async () => {
    mockBackend();
    const from = "/notebooks/0123456789abcdef0123456789abcdef";
    renderLogin({ pathname: "/login", state: { from: { pathname: from } } });

    signIn("demo@example.com", "demo");

    await screen.findByText("Signed-in area");
    expect(screen.getByTestId("location").textContent).toBe(from);
  });

  it("ignores a return path outside the app's note pages", async () => {
    mockBackend();
    renderLogin({ pathname: "/login", state: { from: { pathname: "//evil.example.com" } } });

    signIn("demo@example.com", "demo");

    await screen.findByText("Signed-in area");
    expect(screen.getByTestId("location").textContent).toBe("/notes");
  });

  it("skips the form when a session already exists", async () => {
    mockFetch(({ path }) => (path === "/api/auth/check" ? jsonResponse({ user: { id: "u1", email: "demo@example.com", displayName: "", isAdmin: false, createdTime: 0 } }) : undefined));
    renderLogin();

    expect(await screen.findByText("Signed-in area")).toBeTruthy();
    expect(screen.getByTestId("location").textContent).toBe("/notes");
  });

  it("asks for the authenticator code when the account has two-factor sign-in", async () => {
    const calls = mockBackend();
    renderLogin();
    await waitFor(() => expect(calls.some((c) => c.path === "/api/auth/check")).toBe(true));
    expect(screen.queryByLabelText("Authentication code")).toBeNull();

    signIn("tfa@example.com", "demo");
    const code = await screen.findByLabelText("Authentication code");
    // Asking for the code is not an error
    expect(screen.queryByText("Enter the code from your authenticator app")).toBeNull();

    fireEvent.change(code, { target: { value: "000000" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign In" }));
    expect(await screen.findByText("The code is wrong or was already used")).toBeTruthy();

    fireEvent.change(screen.getByLabelText("Authentication code"), { target: { value: "123456" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign In" }));
    expect(await screen.findByText("Signed-in area")).toBeTruthy();
    expect(calls.filter((c) => c.path === "/api/auth/login").map((c) => (c.body as { code?: string }).code)).toEqual([undefined, "000000", "123456"]);
  });
});
