import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { FolderOpen, Loader2, Upload } from "lucide-react";
import { useImportNotes } from "@/hooks/use-mutations";
import { ImportResult } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";

const plural = (count: number, word: string) => `${count} ${word}${count === 1 ? "" : "s"}`;

/**
 * Imports a ZIP of Markdown files (the export's format, or e.g. an Obsidian vault) into a new notebook,
 * then says what was created and what was left out.
 */
export function ImportDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate();
  const importNotes = useImportNotes();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const close = (value: boolean) => {
    if (!value) {
      setFile(null);
      setResult(null);
      setError(null);
      importNotes.reset();
    }
    onOpenChange(value);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!file) return;
    setError(null);
    importNotes.mutate(file, { onSuccess: setResult, onError: (err) => setError(err.message) });
  };

  const openNotebook = () => {
    if (!result) return;
    navigate(`/notebooks/${result.notebookId}`);
    close(false);
  };

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent className="max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Import notes</DialogTitle>
          <DialogDescription>
            A ZIP of Markdown files, such as an export from Notes or an Obsidian vault. Folders become notebooks; everything
            goes into a new notebook, so nothing you have is changed.
          </DialogDescription>
        </DialogHeader>

        {result ? (
          <div className="grid gap-3 min-h-0" role="status">
            <p className="text-sm text-foreground">
              Imported {plural(result.notes, "note")}, {plural(result.notebooks, "notebook")} and{" "}
              {plural(result.attachments, "attachment")} into <span className="font-semibold">{result.notebookTitle}</span>.
            </p>
            {result.skippedCount > 0 && (
              <div className="grid gap-1.5 min-h-0">
                <p className="text-xs text-muted-foreground">{plural(result.skippedCount, "file")} left out:</p>
                <ul className="max-h-40 overflow-y-auto rounded-lg border border-border/40 bg-muted/20 px-3 py-2 text-xs space-y-1" aria-label="Left out">
                  {result.skipped.map((skip) => (
                    <li key={skip.path} className="break-all">
                      <span className="font-mono text-foreground">{skip.path}</span>
                      <span className="text-muted-foreground">: {skip.reason}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => close(false)}>
                Close
              </Button>
              <Button type="button" onClick={openNotebook}>
                <FolderOpen className="h-4 w-4" />
                Open notebook
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <form className="grid gap-4" onSubmit={handleSubmit}>
            <div className="grid gap-1.5">
              <Label htmlFor="import-file">ZIP file</Label>
              <input
                id="import-file"
                type="file"
                accept=".zip,application/zip"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="text-sm text-muted-foreground file:mr-3 file:rounded-md file:border file:border-border file:bg-background file:px-3 file:py-1.5 file:text-sm file:text-foreground hover:file:bg-muted"
              />
            </div>
            {error && (
              <p role="alert" className="text-xs text-destructive">
                {error}
              </p>
            )}
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => close(false)} disabled={importNotes.isPending}>
                Cancel
              </Button>
              <Button type="submit" disabled={!file || importNotes.isPending}>
                {importNotes.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                Import
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
