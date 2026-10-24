import { useQuery } from "@tanstack/react-query";
import { getSettings } from "@/lib/api";
import { ServerSettings } from "@/lib/types";

export const SETTINGS_QUERY_KEY = ["settings"] as const;

/**
 * Server settings the UI shows to the user. They only change when the server restarts, so they are
 * fetched once per session (only while `enabled`).
 */
export function useServerSettings(enabled = true) {
  return useQuery<ServerSettings, Error>({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: getSettings,
    staleTime: Infinity,
    enabled,
  });
}

/**
 * How long the trash keeps items, for the trash view ("30 days"), or null when it keeps them until
 * they are deleted by hand.
 */
export function trashRetention(days: number | undefined): string | null {
  if (!days || days <= 0) return null;
  return days === 1 ? "1 day" : `${days} days`;
}
