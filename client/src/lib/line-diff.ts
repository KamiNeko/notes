/** One line of a diff: unchanged, only in the old text (removed) or only in the new text (added). */
export interface DiffLine {
  type: "same" | "removed" | "added";
  text: string;
}

/** A run of unchanged lines folded away in the compact view. */
export interface DiffGap {
  type: "gap";
  count: number;
}

/**
 * Above this many cells (changed old lines × changed new lines) the LCS table would take too much memory,
 * so the changed middle is shown as removed and added as a whole.
 */
const MAX_CELLS = 4_000_000;

/**
 * Line diff from `oldText` to `newText` (longest common subsequence). The common start and end are
 * matched first, so typical edits to a long note stay cheap.
 */
export function diffLines(oldText: string, newText: string): DiffLine[] {
  const a = splitLines(oldText);
  const b = splitLines(newText);
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) start++;
  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--;
    endB--;
  }

  const result: DiffLine[] = a.slice(0, start).map((text) => ({ type: "same", text }));
  const midA = a.slice(start, endA);
  const midB = b.slice(start, endB);
  if (midA.length * midB.length > MAX_CELLS) {
    result.push(...midA.map((text): DiffLine => ({ type: "removed", text })));
    result.push(...midB.map((text): DiffLine => ({ type: "added", text })));
  } else {
    result.push(...diffMiddle(midA, midB));
  }
  result.push(...a.slice(endA).map((text): DiffLine => ({ type: "same", text })));
  return result;
}

function splitLines(text: string): string[] {
  return text === "" ? [] : text.replace(/\r\n?/g, "\n").split("\n");
}

/** LCS table from the end, then one walk from the start; removals come before additions at a change. */
function diffMiddle(a: string[], b: string[]): DiffLine[] {
  const n = a.length;
  const m = b.length;
  const width = m + 1;
  const lcs = new Uint32Array((n + 1) * width);
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i * width + j] =
        a[i] === b[j] ? lcs[(i + 1) * width + j + 1] + 1 : Math.max(lcs[(i + 1) * width + j], lcs[i * width + j + 1]);
    }
  }
  const lines: DiffLine[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      lines.push({ type: "same", text: a[i] });
      i++;
      j++;
    } else if (lcs[(i + 1) * width + j] >= lcs[i * width + j + 1]) {
      lines.push({ type: "removed", text: a[i++] });
    } else {
      lines.push({ type: "added", text: b[j++] });
    }
  }
  while (i < n) lines.push({ type: "removed", text: a[i++] });
  while (j < m) lines.push({ type: "added", text: b[j++] });
  return lines;
}

/**
 * Folds runs of unchanged lines, keeping `context` lines next to each change. A text without changes
 * folds completely.
 */
export function foldUnchanged(lines: DiffLine[], context = 3): (DiffLine | DiffGap)[] {
  const changed = lines.map((line) => line.type !== "same");
  const keep = lines.map((_, index) => {
    for (let k = Math.max(0, index - context); k <= Math.min(lines.length - 1, index + context); k++) {
      if (changed[k]) return true;
    }
    return false;
  });
  const result: (DiffLine | DiffGap)[] = [];
  for (let index = 0; index < lines.length; index++) {
    if (keep[index]) {
      result.push(lines[index]);
      continue;
    }
    const last = result[result.length - 1];
    if (last?.type === "gap") last.count++;
    else result.push({ type: "gap", count: 1 });
  }
  return result;
}
