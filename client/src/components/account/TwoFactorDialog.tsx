import { useState } from "react";
import { toast } from "sonner";
import { Copy, Download, Loader2, ShieldCheck, ShieldOff } from "lucide-react";
import {
  useDisableTwoFactor,
  useEnableTwoFactor,
  useNewRecoveryCodes,
  useStartTwoFactorSetup,
  useTwoFactorStatus,
} from "@/hooks/use-two-factor";
import { TwoFactorSetup } from "@/lib/types";
import { QueryError } from "@/components/common/QueryError";
import { Button } from "@kamineko/ui/components/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@kamineko/ui/components/dialog";
import { Input } from "@kamineko/ui/components/input";
import { Label } from "@kamineko/ui/components/label";
import { Skeleton } from "@kamineko/ui/components/skeleton";
import { QrCode } from "./QrCode";

type Step =
  | { kind: "overview" }
  | { kind: "password"; purpose: "setup" | "renew" }
  | { kind: "scan"; setup: TwoFactorSetup }
  | { kind: "codes"; codes: string[] }
  | { kind: "disable" };

/**
 * Two-factor sign-in for the signed-in user: set up an authenticator app (QR code or key, confirmed with
 * a code), keep the recovery codes, get new ones, or turn it off again.
 */
export function TwoFactorDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const status = useTwoFactorStatus(open);
  const [step, setStep] = useState<Step>({ kind: "overview" });
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const startSetup = useStartTwoFactorSetup();
  const enable = useEnableTwoFactor();
  const renew = useNewRecoveryCodes();
  const disable = useDisableTwoFactor();
  const pending = startSetup.isPending || enable.isPending || renew.isPending || disable.isPending;

  const go = (next: Step) => {
    setStep(next);
    setPassword("");
    setCode("");
    setError(null);
  };

  const close = (value: boolean) => {
    if (!value) go({ kind: "overview" });
    onOpenChange(value);
  };

  const onError = (err: Error) => setError(err.message);

  const submitPassword = (purpose: "setup" | "renew") => (e: React.FormEvent) => {
    e.preventDefault();
    if (purpose === "setup") startSetup.mutate(password, { onSuccess: (setup) => go({ kind: "scan", setup }), onError });
    else renew.mutate(password, { onSuccess: (codes) => go({ kind: "codes", codes }), onError });
  };

  const submitCode = (e: React.FormEvent) => {
    e.preventDefault();
    enable.mutate(code, {
      onSuccess: (codes) => {
        toast.success("Two-factor sign-in is on");
        go({ kind: "codes", codes });
      },
      onError,
    });
  };

  const submitDisable = (e: React.FormEvent) => {
    e.preventDefault();
    disable.mutate(
      { password, code },
      {
        onSuccess: () => {
          toast.success("Two-factor sign-in is off");
          go({ kind: "overview" });
        },
        onError,
      }
    );
  };

  const errorLine = error && (
    <p role="alert" className="text-xs text-destructive">
      {error}
    </p>
  );
  const cancel = (
    <Button type="button" variant="ghost" onClick={() => go({ kind: "overview" })} disabled={pending}>
      Cancel
    </Button>
  );
  const passwordField = (
    <div className="grid gap-1.5">
      <Label htmlFor="tfa-password">Your password</Label>
      <Input id="tfa-password" type="password" autoComplete="current-password" autoFocus required value={password} onChange={(e) => setPassword(e.target.value)} />
    </div>
  );

  let content: React.ReactNode;
  if (step.kind === "password") {
    content = (
      <form className="grid gap-4" onSubmit={submitPassword(step.purpose)}>
        <p className="text-sm text-muted-foreground">
          {step.purpose === "setup" ? "Confirm it's you to set up an authenticator app." : "Confirm it's you. Your current recovery codes will stop working."}
        </p>
        {passwordField}
        {errorLine}
        <DialogFooter>
          {cancel}
          <Button type="submit" disabled={pending || !password}>
            {pending && <Loader2 className="h-4 w-4 animate-spin" />}
            Continue
          </Button>
        </DialogFooter>
      </form>
    );
  } else if (step.kind === "scan") {
    content = (
      <form className="grid gap-4" onSubmit={submitCode}>
        <p className="text-sm text-muted-foreground">
          Scan the code with an authenticator app (such as Aegis, Google Authenticator, 1Password or Bitwarden), then enter the 6-digit code it shows.
        </p>
        <div className="flex flex-col items-center gap-2">
          <QrCode value={step.setup.uri} label="QR code for your authenticator app" />
          <p className="text-xs text-muted-foreground">Or enter this key by hand:</p>
          <code className="rounded-md bg-muted px-2 py-1 font-mono text-xs tracking-wider text-foreground text-center" aria-label="Setup key">
            {step.setup.secret.match(/.{1,4}/g)?.join(" ")}
          </code>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="tfa-code">Code from the app</Label>
          <Input id="tfa-code" inputMode="numeric" autoComplete="one-time-code" placeholder="123456" value={code} onChange={(e) => setCode(e.target.value)} />
        </div>
        {errorLine}
        <DialogFooter>
          {cancel}
          <Button type="submit" disabled={pending || !code.trim()}>
            {pending && <Loader2 className="h-4 w-4 animate-spin" />}
            Turn on
          </Button>
        </DialogFooter>
      </form>
    );
  } else if (step.kind === "codes") {
    content = <RecoveryCodes codes={step.codes} onDone={() => go({ kind: "overview" })} />;
  } else if (step.kind === "disable") {
    content = (
      <form className="grid gap-4" onSubmit={submitDisable}>
        <p className="text-sm text-muted-foreground">Signing in will only need your password again.</p>
        {passwordField}
        <div className="grid gap-1.5">
          <Label htmlFor="tfa-disable-code">Code from the app or a recovery code</Label>
          <Input id="tfa-disable-code" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} />
        </div>
        {errorLine}
        <DialogFooter>
          {cancel}
          <Button type="submit" variant="destructive" disabled={pending || !password || !code.trim()}>
            {pending && <Loader2 className="h-4 w-4 animate-spin" />}
            Turn off
          </Button>
        </DialogFooter>
      </form>
    );
  } else if (status.isError) {
    content = <QueryError message="Couldn't load the status" onRetry={() => void status.refetch()} isRetrying={status.isFetching} />;
  } else if (!status.data) {
    content = <Skeleton className="h-16 w-full" />;
  } else if (status.data.enabled) {
    const left = status.data.recoveryCodesLeft;
    content = (
      <div className="grid gap-4">
        <div className="flex items-start gap-3 rounded-lg border border-border/40 bg-muted/20 p-3">
          <ShieldCheck className="h-5 w-5 shrink-0 text-success" />
          <div className="text-sm">
            <p className="font-medium text-foreground">On</p>
            <p className={left <= 2 ? "text-destructive" : "text-muted-foreground"}>
              {left === 1 ? "1 recovery code left." : `${left} recovery codes left.`}
            </p>
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" onClick={() => go({ kind: "password", purpose: "renew" })}>
            New recovery codes
          </Button>
          <Button type="button" variant="destructive" onClick={() => go({ kind: "disable" })}>
            <ShieldOff className="h-4 w-4" />
            Turn off
          </Button>
        </DialogFooter>
      </div>
    );
  } else {
    content = (
      <div className="grid gap-4">
        <p className="text-sm text-muted-foreground">
          Off. With two-factor sign-in, a stolen password alone can't open your notes: signing in also needs a code from an app on your phone.
        </p>
        <DialogFooter>
          <Button type="button" onClick={() => go({ kind: "password", purpose: "setup" })}>
            <ShieldCheck className="h-4 w-4" />
            Set up
          </Button>
        </DialogFooter>
      </div>
    );
  }

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent className="max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Two-factor sign-in</DialogTitle>
          <DialogDescription>A code from an authenticator app as a second step after your password.</DialogDescription>
        </DialogHeader>
        {content}
      </DialogContent>
    </Dialog>
  );
}

/** Shows new recovery codes once, to copy or save as a file. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const text = codes.join("\n");
  const copy = () =>
    navigator.clipboard?.writeText(text).then(
      () => toast.success("Recovery codes copied"),
      () => toast.error("Could not copy")
    );
  const download = () => {
    const url = URL.createObjectURL(new Blob([`Notes recovery codes (each works once)\n\n${text}\n`], { type: "text/plain" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = "notes-recovery-codes.txt";
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };

  return (
    <div className="grid gap-4">
      <p className="text-sm text-muted-foreground">
        Keep these recovery codes somewhere safe, such as your password manager. Each one signs you in once if you lose your phone. They are shown only now.
      </p>
      <ul className="grid grid-cols-2 gap-x-6 gap-y-1 rounded-lg border border-border/40 bg-muted/20 p-3 font-mono text-sm text-foreground" aria-label="Recovery codes">
        {codes.map((c) => (
          <li key={c}>{c}</li>
        ))}
      </ul>
      <DialogFooter className="gap-2">
        <Button type="button" variant="outline" onClick={() => void copy()}>
          <Copy className="h-4 w-4" />
          Copy
        </Button>
        <Button type="button" variant="outline" onClick={download}>
          <Download className="h-4 w-4" />
          Download
        </Button>
        <Button type="button" onClick={onDone}>
          I saved them
        </Button>
      </DialogFooter>
    </div>
  );
}
