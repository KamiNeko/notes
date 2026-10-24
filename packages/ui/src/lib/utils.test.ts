import { describe, expect, it } from "vitest";
import { cn } from "./utils";

describe("cn", () => {
  it("joins conditional classes", () => {
    const hidden = false;
    expect(cn("a", hidden && "b", undefined, "c")).toBe("a c");
  });

  it("lets later Tailwind classes win over conflicting earlier ones", () => {
    expect(cn("px-2 text-sm", "px-4")).toBe("text-sm px-4");
  });
});
