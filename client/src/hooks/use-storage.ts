import { useQuery } from "@tanstack/react-query";
import { getStorage } from "@/lib/api";
import { StorageUsage } from "@/lib/types";

export const STORAGE_QUERY_KEY = ["storage"] as const;

/**
 * The space the signed-in user's attachments take and their limit. Only fetched while `enabled` (the account
 * menu is open), and fresh each time it opens.
 */
export function useStorage(enabled = true) {
  return useQuery<StorageUsage, Error>({
    queryKey: STORAGE_QUERY_KEY,
    queryFn: getStorage,
    enabled,
    staleTime: 0,
  });
}
