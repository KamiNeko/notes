import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { disableTwoFactor, enableTwoFactor, getTwoFactorStatus, newRecoveryCodes, resetUserTwoFactor, startTwoFactorSetup } from "@/lib/api";
import { TwoFactorStatus } from "@/lib/types";
import { USERS_QUERY_KEY } from "./use-users";

export const TWO_FACTOR_QUERY_KEY = ["two-factor"] as const;

/**
 * The signed-in user's two-factor status. Only fetched while `enabled` (the dialog is open).
 */
export function useTwoFactorStatus(enabled = true) {
  return useQuery<TwoFactorStatus, Error>({
    queryKey: TWO_FACTOR_QUERY_KEY,
    queryFn: getTwoFactorStatus,
    enabled,
    staleTime: 0,
  });
}

export function useStartTwoFactorSetup() {
  return useMutation({ mutationFn: startTwoFactorSetup });
}

export function useEnableTwoFactor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: enableTwoFactor,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: TWO_FACTOR_QUERY_KEY }),
  });
}

export function useNewRecoveryCodes() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: newRecoveryCodes,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: TWO_FACTOR_QUERY_KEY }),
  });
}

export function useDisableTwoFactor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ password, code }: { password: string; code: string }) => disableTwoFactor(password, code),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: TWO_FACTOR_QUERY_KEY }),
  });
}

/**
 * Turns two-factor sign-in off for another user (administrators).
 */
export function useResetUserTwoFactor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: resetUserTwoFactor,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY }),
  });
}
