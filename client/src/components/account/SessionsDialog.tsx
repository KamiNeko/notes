import { toast } from "sonner";
import { Loader2, LogOut, MonitorSmartphone } from "lucide-react";
import { useEndOtherSessions, useEndSession, useSessions } from "@/hooks/use-sessions";
import { formatRelativeTime } from "@/lib/format";
import { describeUserAgent } from "@/lib/user-agent";
import { QueryError } from "@/components/common/QueryError";
import { Badge } from "@kamineko/ui/components/badge";
import { Button } from "@kamineko/ui/components/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@kamineko/ui/components/dialog";
import { Skeleton } from "@kamineko/ui/components/skeleton";

/**
 * Where the account is signed in. A lost or shared device can be signed out here without
 * changing the password; this device signs out through the account menu.
 */
export function SessionsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const sessionsQuery = useSessions(open);
  const endSession = useEndSession();
  const endOthers = useEndOtherSessions();
  const sessions = sessionsQuery.data ?? [];
  const others = sessions.filter((s) => !s.current).length;

  const handleEnd = (id: string, label: string) => {
    endSession.mutate(id, {
      onSuccess: () => toast.success(`Signed out ${label}`),
      onError: (err) => toast.error("Could not sign out the session", { description: err.message }),
    });
  };

  const handleEndOthers = () => {
    endOthers.mutate(undefined, {
      onSuccess: (ended) => toast.success(ended === 1 ? "Signed out 1 other session" : `Signed out ${ended} other sessions`),
      onError: (err) => toast.error("Could not sign out the other sessions", { description: err.message }),
    });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Signed-in devices</DialogTitle>
          <DialogDescription>Sign out a device you lost or no longer use. Your password stays the same.</DialogDescription>
        </DialogHeader>

        {/* A plain scroll container: the ScrollArea viewport sizes to its content and would push the buttons out at phone width */}
        <div className="min-h-0 flex-1 overflow-y-auto -mx-1">
          <ul className="space-y-1.5 px-1" aria-label="Sessions">
            {sessionsQuery.isLoading ? (
              <li className="space-y-2 py-2">
                <Skeleton className="h-12 w-full" />
                <Skeleton className="h-12 w-full" />
              </li>
            ) : sessionsQuery.isError ? (
              <li>
                <QueryError compact message="Couldn't load sessions" onRetry={() => void sessionsQuery.refetch()} isRetrying={sessionsQuery.isFetching} />
              </li>
            ) : (
              sessions.map((session) => {
                const label = describeUserAgent(session.userAgent);
                const ending = endSession.isPending && endSession.variables === session.id;
                return (
                  <li key={session.id} className="flex items-center gap-3 rounded-lg border border-border/40 bg-card/30 px-3 py-2">
                    <MonitorSmartphone className="h-4 w-4 shrink-0 text-muted-foreground" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-foreground truncate" title={session.userAgent || undefined}>
                        {label}
                      </p>
                      <p className="text-xs text-muted-foreground truncate">
                        Last active {formatRelativeTime(session.lastSeenTime)} · signed in {formatRelativeTime(session.createdTime)}
                      </p>
                    </div>
                    {session.current ? (
                      <Badge variant="outline" className="text-[10px] shrink-0">This device</Badge>
                    ) : (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-8 shrink-0 gap-1.5 text-destructive hover:text-destructive"
                        onClick={() => handleEnd(session.id, label)}
                        disabled={ending}
                        aria-label={`Sign out ${label}`}
                      >
                        {ending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <LogOut className="h-3.5 w-3.5" />}
                        Sign out
                      </Button>
                    )}
                  </li>
                );
              })
            )}
          </ul>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={handleEndOthers} disabled={others === 0 || endOthers.isPending}>
            {endOthers.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            Sign out all other devices
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
