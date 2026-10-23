import { useState } from "react";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { useChangePassword } from "@/hooks/use-users";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

/** Same minimum as the server */
export const MIN_PASSWORD_LENGTH = 12;

/**
 * Changes the signed-in user's password. Other devices signed in to the account are signed out.
 */
export function ChangePasswordDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const changePassword = useChangePassword();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");
  const [error, setError] = useState<string | null>(null);

  const close = (value: boolean) => {
    if (!value) {
      setCurrent("");
      setNext("");
      setRepeat("");
      setError(null);
    }
    onOpenChange(value);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (next.length < MIN_PASSWORD_LENGTH) {
      setError(`The new password must have at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }
    if (next !== repeat) {
      setError("The new passwords don't match.");
      return;
    }
    setError(null);
    changePassword.mutate(
      { currentPassword: current, newPassword: next },
      {
        onSuccess: () => {
          toast.success("Password changed", { description: "Other devices were signed out." });
          close(false);
        },
        onError: (err) => setError(err.message),
      }
    );
  };

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent>
        <form className="grid gap-4" onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Change password</DialogTitle>
            <DialogDescription>Other devices signed in to your account will be signed out.</DialogDescription>
          </DialogHeader>
          {error && (
            <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">
              {error}
            </p>
          )}
          <div className="grid gap-1.5">
            <Label htmlFor="current-password">Current password</Label>
            <Input
              id="current-password"
              type="password"
              autoComplete="current-password"
              autoFocus
              required
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="new-password">New password</Label>
            <Input
              id="new-password"
              type="password"
              autoComplete="new-password"
              required
              minLength={MIN_PASSWORD_LENGTH}
              value={next}
              onChange={(e) => setNext(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="repeat-password">Repeat new password</Label>
            <Input
              id="repeat-password"
              type="password"
              autoComplete="new-password"
              required
              value={repeat}
              onChange={(e) => setRepeat(e.target.value)}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => close(false)} disabled={changePassword.isPending}>
              Cancel
            </Button>
            <Button type="submit" disabled={changePassword.isPending || !current || !next || !repeat}>
              {changePassword.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Change password
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
