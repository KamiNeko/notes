import { act, cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useTheme } from "../hooks/use-theme";
import { ThemeProvider } from "./ThemeProvider";
import { ThemeToggle } from "./ThemeToggle";

function stubSystemTheme(dark: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: dark && query === "(prefers-color-scheme: dark)",
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

function CurrentTheme() {
  const { theme, isDark } = useTheme();
  return <p>{`${theme}:${isDark ? "dark" : "light"}`}</p>;
}

describe("ThemeProvider", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.className = "";
    stubSystemTheme(false);
  });

  afterEach(() => {
    cleanup();
    document.documentElement.className = "";
  });

  it("applies the default theme to <html>", () => {
    render(
      <ThemeProvider defaultTheme="light" storageKey="test-theme">
        <CurrentTheme />
      </ThemeProvider>
    );
    expect(document.documentElement).toHaveProperty("className", "light");
    expect(screen.getByText("light:light")).toBeTruthy();
  });

  it("restores the stored theme and resolves 'system' from the media query", () => {
    localStorage.setItem("test-theme", "system");
    stubSystemTheme(true);
    render(
      <ThemeProvider defaultTheme="light" storageKey="test-theme">
        <CurrentTheme />
      </ThemeProvider>
    );
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(screen.getByText("system:dark")).toBeTruthy();
  });

  it("toggles and remembers the choice under the app's storage key", () => {
    render(
      <ThemeProvider defaultTheme="dark" storageKey="test-theme">
        <ThemeToggle />
        <CurrentTheme />
      </ThemeProvider>
    );
    act(() => screen.getByRole("button", { name: "Switch to light mode" }).click());
    expect(localStorage.getItem("test-theme")).toBe("light");
    expect(document.documentElement.classList.contains("light")).toBe(true);
    expect(screen.getByText("light:light")).toBeTruthy();
  });
});
