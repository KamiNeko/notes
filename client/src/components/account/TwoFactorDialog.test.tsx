import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { jsonResponse, mockFetch } from "@/test/fetch-mock";
import { renderWithProviders } from "@/test/render";
import { TwoFactorDialog } from "./TwoFactorDialog";

afterEach(cleanup);

const SECRET = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
const CODES = ["AAAAA-BBBBB", "CCCCC-DDDDD"];

describe("TwoFactorDialog", () => {
  it("sets up an authenticator app and shows the recovery codes once", async () => {
    let enabled = false;
    const calls = mockFetch(({ method, path, body }) => {
      if (path === "/api/auth/2fa") return jsonResponse({ enabled, recoveryCodesLeft: enabled ? 2 : 0 });
      if (method === "POST" && path === "/api/auth/2fa/setup") {
        return (body as { password: string }).password === "right-password"
          ? jsonResponse({ secret: SECRET, uri: `otpauth://totp/Notes:me?secret=${SECRET}&issuer=Notes` })
          : jsonResponse({ error: "The password is wrong", status: 400 }, 400);
      }
      if (method === "POST" && path === "/api/auth/2fa/enable") {
        enabled = true;
        return jsonResponse({ recoveryCodes: CODES });
      }
      return undefined;
    });
    renderWithProviders(<TwoFactorDialog open onOpenChange={() => undefined} />);

    fireEvent.click(await screen.findByRole("button", { name: "Set up" }));
    fireEvent.change(screen.getByLabelText("Your password"), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The password is wrong");
    fireEvent.change(screen.getByLabelText("Your password"), { target: { value: "right-password" } });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(await screen.findByRole("img", { name: "QR code for your authenticator app" })).toBeTruthy();
    expect(screen.getByLabelText("Setup key").textContent).toBe("JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP");
    fireEvent.change(screen.getByLabelText("Code from the app"), { target: { value: "123456" } });
    fireEvent.click(screen.getByRole("button", { name: "Turn on" }));

    const codes = await screen.findByRole("list", { name: "Recovery codes" });
    expect(within(codes).getAllByRole("listitem").map((li) => li.textContent)).toEqual(CODES);
    expect(calls.find((c) => c.path === "/api/auth/2fa/enable")?.body).toEqual({ code: "123456" });

    fireEvent.click(screen.getByRole("button", { name: "I saved them" }));
    expect(await screen.findByText("2 recovery codes left.")).toBeTruthy();
  });

  it("turns two-factor sign-in off with the password and a code", async () => {
    let enabled = true;
    const calls = mockFetch(({ method, path }) => {
      if (path === "/api/auth/2fa") return jsonResponse({ enabled, recoveryCodesLeft: enabled ? 7 : 0 });
      if (method === "POST" && path === "/api/auth/2fa/disable") {
        enabled = false;
        return new Response(null, { status: 204 });
      }
      return undefined;
    });
    renderWithProviders(<TwoFactorDialog open onOpenChange={() => undefined} />);

    expect(await screen.findByText("7 recovery codes left.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Turn off" }));
    fireEvent.change(screen.getByLabelText("Your password"), { target: { value: "pw" } });
    fireEvent.change(screen.getByLabelText("Code from the app or a recovery code"), { target: { value: "AAAAA-BBBBB" } });
    fireEvent.click(screen.getByRole("button", { name: "Turn off" }));

    expect(await screen.findByRole("button", { name: "Set up" })).toBeTruthy();
    expect(calls.find((c) => c.path === "/api/auth/2fa/disable")?.body).toEqual({ password: "pw", code: "AAAAA-BBBBB" });
  });
});
