import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { endOtherSessions, endSession, getSessions } from "@/lib/api";
import { SignInSession } from "@/lib/types";

export const SESSIONS_QUERY_KEY = ["sessions"] as const;

/**
 * Where the signed-in user is signed in. Only fetched while `enabled` (the sessions dialog is open).
 */
export function useSessions(enabled = true) {
  return useQuery<SignInSession[], Error>({
    queryKey: SESSIONS_QUERY_KEY,
    queryFn: getSessions,
    enabled,
  });
}

/**
 * Signs out one session.
 */
export function useEndSession() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: endSession,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY }),
  });
}

/**
 * Signs out every other session of the user.
 */
export function useEndOtherSessions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: endOtherSessions,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY }),
  });
}
