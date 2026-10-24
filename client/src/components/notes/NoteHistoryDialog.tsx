import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ArrowLeft, History, Loader2, RotateCcw } from "lucide-react";
import { ApiError } from "@/lib/api";
import { MARKUP_HTML, Note } from "@/lib/types";
import { formatRelativeTime } from "@/lib/format";
import { cn } from "@kamineko/ui/lib/utils";
import { useNoteVersion, useNoteVersions, useRestoreNoteVersion } from "@/hooks/use-versions";
import { MarkdownRenderer } from "@/components/common/MarkdownRenderer";
import { VersionChanges } from "./VersionChanges";
import { QueryError } from "@/components/common/QueryError";
import { Button } from "@kamineko/ui/components/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@kamineko/ui/components/dialog";
import { ScrollArea } from "@kamineko/ui/components/scroll-area";
import { Skeleton } from "@kamineko/ui/components/skeleton";

const formatDateTime = (ms: number) => new Date(ms).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

interface NoteHistoryDialogProps {
  note: Note;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Version history of a note: earlier states (saved before an editing session changed them), each
 * with a rendered preview, the changes from it to the current note, and Restore. Restoring keeps the replaced text as a version as well, so it
 * can be undone from here. Side by side on wide screens; on phones the preview replaces the list.
 */
export function NoteHistoryDialog({ note, open, onOpenChange }: NoteHistoryDialogProps) {
  const queryClient = useQueryClient();
  const versionsQuery = useNoteVersions(note.id, open);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState<"preview" | "changes">("preview");
  const versionQuery = useNoteVersion(note.id, selectedId);
  const restore = useRestoreNoteVersion();
  const versions = versionsQuery.data ?? [];
  const selected = versionQuery.data;

  const handleRestore = () => {
    if (!selectedId) return;
    restore.mutate(
      { noteId: note.id, versionId: selectedId, baseUpdatedTime: note.updatedTime },
      {
        onSuccess: () => {
          toast.success("Version restored", { description: "The replaced text is kept in the history." });
          setSelectedId(null);
          onOpenChange(false);
        },
        onError: (err) => {
          if (err instanceof ApiError && err.status === 409) {
            void queryClient.invalidateQueries({ queryKey: ["note", note.id] });
            toast.error("The note was changed meanwhile", { description: "It was reloaded. Open the history again to restore." });
            onOpenChange(false);
            return;
          }
          toast.error("Could not restore version", { description: err.message });
        },
      }
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl h-[85vh] max-h-[720px] flex flex-col gap-0 p-0 overflow-hidden">
        <DialogHeader className="px-5 pt-5 pb-3 border-b border-border/40 space-y-1">
          <DialogTitle className="flex items-center gap-2">
            <History className="h-4 w-4 text-primary" />
            Version history
          </DialogTitle>
          <DialogDescription className="truncate">{note.title || "Untitled Note"}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-1 min-h-0">
          {/* Version list */}
          <div
            className={cn(
              "w-full md:w-64 shrink-0 md:border-r border-border/40 min-h-0 flex-col",
              selectedId ? "hidden md:flex" : "flex"
            )}
          >
            <ScrollArea className="flex-1">
              <ul className="p-2 space-y-1" aria-label="Versions">
                <li className="px-3 py-2 rounded-lg text-xs text-muted-foreground">
                  <p className="font-medium text-foreground">Current version</p>
                  <p>{formatDateTime(note.updatedTime)}</p>
                </li>
                {versionsQuery.isLoading ? (
                  <li className="space-y-2 px-3 py-2">
                    <Skeleton className="h-4 w-3/4" />
                    <Skeleton className="h-4 w-1/2" />
                  </li>
                ) : versionsQuery.isError ? (
                  <li>
                    <QueryError
                      compact
                      message="Couldn't load the history"
                      onRetry={() => void versionsQuery.refetch()}
                      isRetrying={versionsQuery.isFetching}
                    />
                  </li>
                ) : versions.length === 0 ? (
                  <li className="px-3 py-2 text-xs text-muted-foreground italic">
                    No earlier versions yet. A version is kept each time you start editing after a pause.
                  </li>
                ) : (
                  versions.map((version) => (
                    <li key={version.id}>
                      <button
                        type="button"
                        onClick={() => setSelectedId(version.id)}
                        aria-current={selectedId === version.id ? "true" : undefined}
                        className={cn(
                          "w-full text-left px-3 py-2 rounded-lg text-xs transition-colors outline-hidden focus-visible:ring-2 focus-visible:ring-ring",
                          selectedId === version.id
                            ? "bg-accent text-accent-foreground"
                            : "text-muted-foreground hover:bg-muted/60 hover:text-foreground"
                        )}
                      >
                        <p className="font-medium text-foreground" title={formatDateTime(version.updatedTime)}>
                          {formatRelativeTime(version.updatedTime)}
                        </p>
                        <p className="truncate">{version.title || "Untitled Note"}</p>
                        {version.preview && <p className="line-clamp-2 mt-0.5 opacity-80">{version.preview}</p>}
                      </button>
                    </li>
                  ))
                )}
              </ul>
            </ScrollArea>
          </div>

          {/* Preview of the selected version */}
          <div className={cn("flex-1 min-w-0 min-h-0 flex-col", selectedId ? "flex" : "hidden md:flex")}>
            {!selectedId ? (
              <div className="flex flex-1 items-center justify-center p-6 text-center text-sm text-muted-foreground">
                Select a version to see it.
              </div>
            ) : (
              <>
                <div className="flex items-center justify-between gap-2 px-4 py-2.5 border-b border-border/40">
                  <div className="flex items-center gap-2 min-w-0">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 md:hidden shrink-0"
                      onClick={() => setSelectedId(null)}
                      aria-label="Back to versions"
                    >
                      <ArrowLeft className="h-4 w-4" />
                    </Button>
                    <span className="text-xs text-muted-foreground truncate">
                      {selected ? formatDateTime(selected.updatedTime) : "Loading…"}
                    </span>
                  </div>
                  <Button size="sm" className="h-8 shrink-0" onClick={handleRestore} disabled={!selected || restore.isPending}>
                    {restore.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RotateCcw className="h-3.5 w-3.5" />}
                    Restore this version
                  </Button>
                </div>
                <div role="group" aria-label="Show" className="flex items-center gap-1 px-4 py-2 border-b border-border/40">
                  {(["preview", "changes"] as const).map((option) => (
                    <Button
                      key={option}
                      variant="ghost"
                      size="sm"
                      aria-pressed={view === option}
                      onClick={() => setView(option)}
                      className={cn("h-7 px-2.5 text-xs", view === option ? "bg-accent text-accent-foreground" : "text-muted-foreground")}
                    >
                      {option === "preview" ? "Preview" : "Changes"}
                    </Button>
                  ))}
                </div>
                <ScrollArea className="flex-1">
                  <div className="px-5 sm:px-8 py-6 space-y-4">
                    {versionQuery.isError ? (
                      <QueryError
                        message="Couldn't load this version"
                        error={versionQuery.error}
                        onRetry={() => void versionQuery.refetch()}
                        isRetrying={versionQuery.isFetching}
                      />
                    ) : !selected ? (
                      <div className="space-y-3">
                        <Skeleton className="h-7 w-2/3" />
                        <Skeleton className="h-4 w-full" />
                        <Skeleton className="h-4 w-5/6" />
                      </div>
                    ) : view === "changes" ? (
                      <VersionChanges oldTitle={selected.title} oldBody={selected.body} newTitle={note.title} newBody={note.body} />
                    ) : (
                      <>
                        <h2 className="text-2xl font-bold tracking-tight text-foreground">{selected.title || "Untitled Note"}</h2>
                        {selected.body.trim() ? (
                          <MarkdownRenderer content={selected.body} html={note.markupLanguage === MARKUP_HTML} />
                        ) : (
                          <p className="text-sm text-muted-foreground italic">This version is empty.</p>
                        )}
                      </>
                    )}
                  </div>
                </ScrollArea>
              </>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
