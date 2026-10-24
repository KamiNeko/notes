import { describe, expect, it } from "vitest";
import { diffLines, foldUnchanged } from "./line-diff";

const render = (lines: ReturnType<typeof diffLines>) =>
  lines.map((l) => (l.type === "same" ? "  " : l.type === "added" ? "+ " : "- ") + l.text);

describe("diffLines", () => {
  it("marks removed and added lines between unchanged ones", () => {
    expect(render(diffLines("a\nb\nc\nd", "a\nB\nc\nd\ne"))).toEqual(["  a", "- b", "+ B", "  c", "  d", "+ e"]);
  });

  it("finds moved-around lines by their longest common run", () => {
    expect(render(diffLines("x\na\nb\nc", "a\nb\ny\nc"))).toEqual(["- x", "  a", "  b", "+ y", "  c"]);
  });

  it("handles empty texts and Windows line endings", () => {
    expect(diffLines("", "")).toEqual([]);
    expect(render(diffLines("", "new"))).toEqual(["+ new"]);
    expect(render(diffLines("old", ""))).toEqual(["- old"]);
    expect(render(diffLines("a\r\nb", "a\nb"))).toEqual(["  a", "  b"]);
  });

  it("falls back to a block replacement for huge changed regions", () => {
    const old = Array.from({ length: 2500 }, (_, i) => `old ${i}`).join("\n");
    const now = Array.from({ length: 2500 }, (_, i) => `new ${i}`).join("\n");
    const lines = diffLines(`same\n${old}\nend`, `same\n${now}\nend`);
    expect(lines).toHaveLength(5002);
    expect(lines[1]).toEqual({ type: "removed", text: "old 0" });
    expect(lines[2501]).toEqual({ type: "added", text: "new 0" });
    expect(lines[lines.length - 1]).toEqual({ type: "same", text: "end" });
  });
});

describe("foldUnchanged", () => {
  it("keeps context around changes and folds the rest", () => {
    const old = Array.from({ length: 20 }, (_, i) => `line ${i}`);
    const now = [...old];
    now[10] = "changed";
    const folded = foldUnchanged(diffLines(old.join("\n"), now.join("\n")), 2);
    expect(folded.map((l) => (l.type === "gap" ? `… ${l.count}` : l.text))).toEqual([
      "… 8",
      "line 8",
      "line 9",
      "line 10",
      "changed",
      "line 11",
      "line 12",
      "… 7",
    ]);
  });

  it("folds a text without changes completely", () => {
    expect(foldUnchanged(diffLines("a\nb", "a\nb"))).toEqual([{ type: "gap", count: 2 }]);
  });
});
