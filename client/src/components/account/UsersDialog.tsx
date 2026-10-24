import { useState } from "react";
import { toast } from "sonner";
import { KeyRound, Loader2, MoreHorizontal, Shield, ShieldCheck, ShieldOff, ShieldX, Trash2, UserPlus } from "lucide-react";
import { useAuth } from "@/hooks/use-auth";
import { useCreateUser, useDeleteUser, useUpdateUser, useUsers } from "@/hooks/use-users";
import { useResetUserTwoFactor } from "@/hooks/use-two-factor";
import { UserInfo } from "@/lib/types";
import { ConfirmDialog } from "@/components/common/Dialogs";
import { QueryError } from "@/components/common/QueryError";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Skeleton } from "@/components/ui/skeleton";
import { MIN_PASSWORD_LENGTH } from "./ChangePasswordDialog";

/**
 * User administration for administrators: there is no public registration, accounts are added here.
 * Every user sees only their own notes.
 */
export function UsersDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { user: me } = useAuth();
  const usersQuery = useUsers(open);
  const updateUser = useUpdateUser();
  const deleteUser = useDeleteUser();
  const [resetFor, setResetFor] = useState<UserInfo | null>(null);
  const [deleteFor, setDeleteFor] = useState<UserInfo | null>(null);
  const [resetTwoFactorFor, setResetTwoFactorFor] = useState<UserInfo | null>(null);
  const resetTwoFactor = useResetUserTwoFactor();

  const toggleAdmin = (user: UserInfo) => {
    updateUser.mutate(
      { id: user.id, changes: { isAdmin: !user.isAdmin } },
      {
        onSuccess: () => toast.success(user.isAdmin ? `${user.email} is no longer an administrator` : `${user.email} is now an administrator`),
        onError: (err) => toast.error("Could not change the role", { description: err.message }),
      }
    );
  };

  const handleResetTwoFactor = () => {
    if (!resetTwoFactorFor) return;
    const email = resetTwoFactorFor.email;
    resetTwoFactor.mutate(resetTwoFactorFor.id, {
      onSuccess: () => {
        setResetTwoFactorFor(null);
        toast.success(`Two-factor sign-in turned off for ${email}`, { description: "They were signed out everywhere." });
      },
      onError: (err) => toast.error("Could not turn off two-factor sign-in", { description: err.message }),
    });
  };

  const handleDelete = () => {
    if (!deleteFor) return;
    const email = deleteFor.email;
    deleteUser.mutate(deleteFor.id, {
      onSuccess: () => {
        setDeleteFor(null);
        toast.success(`Deleted ${email}`);
      },
      onError: (err) => toast.error("Could not delete user", { description: err.message }),
    });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Users</DialogTitle>
          <DialogDescription>Add accounts for other people. Each user only sees their own notes.</DialogDescription>
        </DialogHeader>

        <AddUserForm />

        <ScrollArea className="min-h-0 flex-1 -mx-1">
          <ul className="space-y-1.5 px-1" aria-label="Accounts">
            {usersQuery.isLoading ? (
              <li className="space-y-2 py-2">
                <Skeleton className="h-10 w-full" />
                <Skeleton className="h-10 w-full" />
              </li>
            ) : usersQuery.isError ? (
              <li>
                <QueryError compact message="Couldn't load users" onRetry={() => void usersQuery.refetch()} isRetrying={usersQuery.isFetching} />
              </li>
            ) : (
              (usersQuery.data ?? []).map((user) => {
                const isMe = user.id === me?.id;
                return (
                  <li key={user.id} className="flex items-center gap-3 rounded-lg border border-border/40 bg-card/30 px-3 py-2">
                    <Avatar className="h-8 w-8">
                      <AvatarFallback className="bg-primary/15 text-primary text-xs font-semibold">
                        {(user.displayName || user.email).charAt(0).toUpperCase()}
                      </AvatarFallback>
                    </Avatar>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-foreground truncate">{user.displayName || user.email}</p>
                      {user.displayName && <p className="text-xs text-muted-foreground truncate">{user.email}</p>}
                    </div>
                    {isMe && <Badge variant="outline" className="text-[10px]">You</Badge>}
                    {user.twoFactor && (
                      <span title="Two-factor sign-in is on" className="shrink-0">
                        <ShieldCheck className="h-4 w-4 text-success" aria-label="Two-factor sign-in is on" />
                      </span>
                    )}
                    {user.isAdmin && (
                      <Badge variant="outline" className="text-[10px] gap-1 border-primary/30 bg-primary/10 text-foreground">
                        <Shield className="h-3 w-3 text-primary" />
                        Admin
                      </Badge>
                    )}
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" className="h-8 w-8 shrink-0" aria-label={`Actions for ${user.email}`}>
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-52">
                        <DropdownMenuItem onClick={() => setResetFor(user)}>
                          <KeyRound className="h-4 w-4" />
                          Set new password
                        </DropdownMenuItem>
                        {user.twoFactor && !isMe && (
                          <DropdownMenuItem onClick={() => setResetTwoFactorFor(user)}>
                            <ShieldX className="h-4 w-4" />
                            Turn off two-factor sign-in
                          </DropdownMenuItem>
                        )}
                        {!isMe && (
                          <DropdownMenuItem onClick={() => toggleAdmin(user)}>
                            {user.isAdmin ? <ShieldOff className="h-4 w-4" /> : <Shield className="h-4 w-4" />}
                            {user.isAdmin ? "Remove administrator" : "Make administrator"}
                          </DropdownMenuItem>
                        )}
                        {!isMe && (
                          <>
                            <DropdownMenuSeparator />
                            <DropdownMenuItem onClick={() => setDeleteFor(user)} className="text-destructive focus:text-destructive">
                              <Trash2 className="h-4 w-4" />
                              Delete user
                            </DropdownMenuItem>
                          </>
                        )}
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </li>
                );
              })
            )}
          </ul>
        </ScrollArea>

        <SetPasswordDialog user={resetFor} onClose={() => setResetFor(null)} />
        <ConfirmDialog
          open={resetTwoFactorFor !== null}
          onOpenChange={(value) => !value && setResetTwoFactorFor(null)}
          title="Turn off two-factor sign-in?"
          description={
            resetTwoFactorFor
              ? `For when ${resetTwoFactorFor.email} lost their authenticator and recovery codes. They will be signed out everywhere and can sign in with their password only, until they set it up again.`
              : undefined
          }
          confirmLabel="Turn off"
          destructive
          isPending={resetTwoFactor.isPending}
          onConfirm={handleResetTwoFactor}
        />
        <ConfirmDialog
          open={deleteFor !== null}
          onOpenChange={(value) => !value && setDeleteFor(null)}
          title="Delete user?"
          description={
            deleteFor ? `${deleteFor.email} and all their notes, notebooks, tags, versions and attachments will be deleted. This cannot be undone.` : undefined
          }
          confirmLabel="Delete user"
          destructive
          isPending={deleteUser.isPending}
          onConfirm={handleDelete}
        />
      </DialogContent>
    </Dialog>
  );
}

function AddUserForm() {
  const createUser = useCreateUser();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [isAdmin, setIsAdmin] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`The password must have at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    setError(null);
    createUser.mutate(
      { email: email.trim(), password, displayName: displayName.trim() || undefined, isAdmin },
      {
        onSuccess: (user) => {
          toast.success(`Added ${user.email}`, { description: "Share the password with them; they can change it after signing in." });
          setEmail("");
          setDisplayName("");
          setPassword("");
          setIsAdmin(false);
        },
        onError: (err) => setError(err.message),
      }
    );
  };

  return (
    <form onSubmit={handleSubmit} className="grid gap-3 rounded-lg border border-border/40 bg-muted/20 p-3" aria-label="Add user">
      <div className="grid gap-3 sm:grid-cols-3">
        <div className="grid gap-1.5">
          <Label htmlFor="new-user-email" className="text-xs">Email</Label>
          <Input id="new-user-email" type="email" required autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="new-user-name" className="text-xs">Name (optional)</Label>
          <Input id="new-user-name" autoComplete="off" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="new-user-password" className="text-xs">Password</Label>
          <Input
            id="new-user-password"
            type="password"
            required
            autoComplete="new-password"
            minLength={MIN_PASSWORD_LENGTH}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>
      </div>
      {error && (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      )}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label className="flex items-center gap-2 text-xs text-muted-foreground cursor-pointer select-none">
          <input type="checkbox" className="h-3.5 w-3.5 accent-primary" checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
          Administrator (can manage users)
        </label>
        <Button type="submit" size="sm" className="h-8" disabled={createUser.isPending || !email.trim() || !password}>
          {createUser.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <UserPlus className="h-3.5 w-3.5" />}
          Add user
        </Button>
      </div>
    </form>
  );
}

function SetPasswordDialog({ user, onClose }: { user: UserInfo | null; onClose: () => void }) {
  const updateUser = useUpdateUser();
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const close = () => {
    setPassword("");
    setError(null);
    onClose();
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!user) return;
    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`The password must have at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    updateUser.mutate(
      { id: user.id, changes: { password } },
      {
        onSuccess: () => {
          toast.success(`New password set for ${user.email}`, { description: "They were signed out everywhere." });
          close();
        },
        onError: (err) => setError(err.message),
      }
    );
  };

  return (
    <Dialog open={user !== null} onOpenChange={(value) => !value && close()}>
      <DialogContent>
        <form className="grid gap-4" onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Set new password</DialogTitle>
            <DialogDescription>{user?.email} will be signed out on all devices.</DialogDescription>
          </DialogHeader>
          {error && (
            <p role="alert" className="text-xs text-destructive">
              {error}
            </p>
          )}
          <div className="grid gap-1.5">
            <Label htmlFor="reset-password">New password</Label>
            <Input
              id="reset-password"
              type="password"
              autoComplete="new-password"
              autoFocus
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={close} disabled={updateUser.isPending}>
              Cancel
            </Button>
            <Button type="submit" disabled={updateUser.isPending || !password}>
              {updateUser.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Set password
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
