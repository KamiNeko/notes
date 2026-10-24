import { useMemo, useState } from "react";
import { diffLines, foldUnchanged } from "@/lib/line-diff";
import { cn } from "@/lib/utils";

interface VersionChangesProps {
  oldTitle: string;
  oldBody: string;
  newTitle: string;
  newBody: string;
}

/**
 * What changed from an earlier version to the current note: the title and a line diff of the body.
 * Lines only in the version are marked removed, lines added since are marked added. Long unchanged
 * stretches are folded and can be shown.
 */
export function VersionChanges({ oldTitle, oldBody, newTitle, newBody }: VersionChangesProps) {
  const [showAll, setShowAll] = useState(false);
  const lines = useMemo(() => diffLines(oldBody, newBody), [oldBody, newBody]);
  const bodyChanged = lines.some((line) => line.type !== "same");
  const titleChanged = oldTitle !== newTitle;
  const rows = showAll ? lines : foldUnchanged(lines);

  if (!bodyChanged && !titleChanged) {
    return <p className="text-sm text-muted-foreground italic">No changes: this version matches the current note.</p>;
  }

  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">
        Compared with the current note. <span className="text-destructive font-medium">−</span> only in this version,{" "}
        <span className="text-success font-medium">+</span> added since.
      </p>
      {titleChanged && (
        <div className="rounded-lg border border-border/40 overflow-hidden text-sm" aria-label="Title changes" role="group">
          <ChangedLine type="removed" text={oldTitle || "Untitled Note"} />
          <ChangedLine type="added" text={newTitle || "Untitled Note"} />
        </div>
      )}
      {bodyChanged ? (
        <ol className="rounded-lg border border-border/40 overflow-hidden font-mono text-xs leading-relaxed" aria-label="Line changes">
          {rows.map((row, index) =>
            row.type === "gap" ? (
              <li key={index}>
                <button
                  type="button"
                  onClick={() => setShowAll(true)}
                  className="w-full px-3 py-1 text-left text-muted-foreground bg-muted/40 hover:bg-muted hover:text-foreground transition-colors outline-hidden focus-visible:ring-2 focus-visible:ring-ring"
                >
                  ⋯ {row.count} unchanged {row.count === 1 ? "line" : "lines"}
                </button>
              </li>
            ) : (
              <li key={index}>
                <ChangedLine type={row.type} text={row.text} />
              </li>
            )
          )}
        </ol>
      ) : (
        <p className="text-sm text-muted-foreground italic">The text is unchanged.</p>
      )}
    </div>
  );
}

const SIGNS = { same: " ", removed: "−", added: "+" } as const;
const LABELS = { same: "", removed: "Removed: ", added: "Added: " } as const;

function ChangedLine({ type, text }: { type: "same" | "removed" | "added"; text: string }) {
  return (
    <div
      className={cn(
        "flex gap-2 px-3 whitespace-pre-wrap break-words",
        type === "removed" && "bg-destructive/10",
        type === "added" && "bg-success/10"
      )}
    >
      <span
        aria-hidden
        className={cn(
          "w-3 shrink-0 select-none text-muted-foreground",
          type === "removed" && "text-destructive",
          type === "added" && "text-success"
        )}
      >
        {SIGNS[type]}
      </span>
      <span className="min-w-0 flex-1 text-foreground">
        {LABELS[type] && <span className="sr-only">{LABELS[type]}</span>}
        {text || " "}
      </span>
    </div>
  );
}
