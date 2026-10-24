import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { VersionChanges } from "./VersionChanges";

afterEach(cleanup);

const many = (n: number) => Array.from({ length: n }, (_, i) => `line ${i}`);

describe("VersionChanges", () => {
  it("folds long unchanged stretches and shows them on request", () => {
    const old = many(30);
    const now = [...old];
    now[15] = "edited";
    render(<VersionChanges oldTitle="T" oldBody={old.join("\n")} newTitle="T" newBody={now.join("\n")} />);

    expect(screen.queryByRole("group", { name: "Title changes" })).toBeNull();
    expect(screen.getByText("edited")).toBeTruthy();
    expect(screen.queryByText("line 0")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "⋯ 12 unchanged lines" }));
    expect(screen.getByText("line 0")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /unchanged/ })).toBeNull();
  });

  it("says when nothing changed", () => {
    render(<VersionChanges oldTitle="T" oldBody="same" newTitle="T" newBody="same" />);
    expect(screen.getByText(/this version matches the current note/)).toBeTruthy();
  });

  it("shows a title-only change", () => {
    render(<VersionChanges oldTitle="Old" oldBody="same" newTitle="New" newBody="same" />);
    expect(screen.getByRole("group", { name: "Title changes" }).textContent).toBe("−Removed: Old+Added: New");
    expect(screen.getByText("The text is unchanged.")).toBeTruthy();
  });
});
