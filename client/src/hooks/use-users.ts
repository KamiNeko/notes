import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { changePassword, createUser, deleteUser, getUsers, updateUser } from "@/lib/api";
import { UserInfo } from "@/lib/types";

export const USERS_QUERY_KEY = ["admin-users"] as const;

/**
 * All accounts (administrators only). Only fetched while `enabled` (the user dialog is open).
 */
export function useUsers(enabled = true) {
  return useQuery<UserInfo[], Error>({
    queryKey: USERS_QUERY_KEY,
    queryFn: getUsers,
    enabled,
  });
}

export function useCreateUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createUser,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY }),
  });
}

export function useUpdateUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, changes }: { id: string; changes: Parameters<typeof updateUser>[1] }) => updateUser(id, changes),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY }),
  });
}

export function useDeleteUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: deleteUser,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY }),
  });
}

/**
 * Changes the signed-in user's own password (signs out their other sessions).
 */
export function useChangePassword() {
  return useMutation({
    mutationFn: ({ currentPassword, newPassword }: { currentPassword: string; newPassword: string }) =>
      changePassword(currentPassword, newPassword),
  });
}
