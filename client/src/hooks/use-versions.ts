import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getNoteVersion, getNoteVersions, restoreNoteVersion } from "@/lib/api";
import { Note, NoteVersion, NoteVersionDetail } from "@/lib/types";
import { updateNoteInLists } from "./use-mutations";

/** Query key of a note's version list; versions of one version are nested under it */
export const versionsQueryKey = (noteId: string) => ["note-versions", noteId] as const;

/**
 * A note's earlier versions, newest first. Only fetched while `enabled` (the history is open).
 */
export function useNoteVersions(noteId: string, enabled = true) {
  return useQuery<NoteVersion[], Error>({
    queryKey: versionsQueryKey(noteId),
    queryFn: () => getNoteVersions(noteId),
    enabled,
    // Every save can add a version, so reopening the history always asks again
    staleTime: 0,
  });
}

/**
 * One version with its body. Versions never change, so it is fetched once.
 */
export function useNoteVersion(noteId: string, versionId: string | null) {
  return useQuery<NoteVersionDetail, Error>({
    queryKey: [...versionsQueryKey(noteId), versionId],
    queryFn: () => getNoteVersion(noteId, versionId!),
    enabled: Boolean(versionId),
    staleTime: Infinity,
  });
}

/**
 * Restores a version's title and body (based on the note's current updatedTime, so a change made
 * elsewhere meanwhile is a 409). Updates the cached note and lists, and reloads the history, which
 * now holds the replaced content too.
 */
export function useRestoreNoteVersion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ noteId, versionId, baseUpdatedTime }: { noteId: string; versionId: string; baseUpdatedTime?: number }) =>
      restoreNoteVersion(noteId, versionId, baseUpdatedTime),
    onSuccess: (note: Note) => {
      queryClient.setQueryData(["note", note.id], note);
      updateNoteInLists(queryClient, note);
      void queryClient.invalidateQueries({ queryKey: versionsQueryKey(note.id), exact: true });
    },
  });
}
