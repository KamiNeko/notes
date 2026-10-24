import React, { useState } from "react";
import { useAuth } from "@/hooks/use-auth";
import { needsSecondFactor } from "@/lib/api";
import { Button } from "@kamineko/ui/components/button";
import { Input } from "@kamineko/ui/components/input";
import { Label } from "@kamineko/ui/components/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@kamineko/ui/components/card";
import { BookOpen, Loader2, Lock, Mail, AlertCircle, ShieldCheck } from "lucide-react";

interface LoginFormProps {
  onSuccess?: () => void;
}

export function LoginForm({ onSuccess }: LoginFormProps) {
  const { login, lastEmail } = useAuth();
  // After an expired session the same account signs in again
  const [email, setEmail] = useState(lastEmail ?? "");
  const [password, setPassword] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // Shown once the server says the account signs in with a second factor
  const [needsCode, setNeedsCode] = useState(false);
  const [code, setCode] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      setErrorMessage("Please enter your email address.");
      return;
    }
    if (!password) {
      setErrorMessage("Please enter your password.");
      return;
    }
    if (needsCode && !code.trim()) {
      setErrorMessage("Please enter the code from your authenticator app.");
      return;
    }

    try {
      setIsSubmitting(true);
      await login(trimmedEmail, password, needsCode ? code : undefined);
      onSuccess?.();
    } catch (err: unknown) {
      if (needsSecondFactor(err)) {
        // The first answer only asks for the code; later ones say it was wrong
        if (needsCode) setErrorMessage((err as Error).message);
        setNeedsCode(true);
        setCode("");
      } else if (err instanceof Error) {
        setErrorMessage(err.message);
      } else {
        setErrorMessage("Authentication failed. Please check your credentials.");
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Card className="w-full max-w-md border-border/60 bg-card/95 backdrop-blur-md shadow-2xl relative overflow-hidden">
      {/* Decorative top accent line */}
      <div className="absolute top-0 left-0 right-0 h-1 bg-primary/80" />

      <CardHeader className="space-y-3 text-center pb-6 pt-8">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary border border-primary/20 shadow-inner">
          <BookOpen className="h-7 w-7 text-primary" />
        </div>
        <div className="space-y-1">
          <CardTitle className="text-2xl font-bold tracking-tight text-foreground">
            Notes
          </CardTitle>
          <CardDescription className="text-sm text-muted-foreground">
            Sign in to your notes and notebooks
          </CardDescription>
        </div>
      </CardHeader>

      <form onSubmit={handleSubmit}>
        <CardContent className="space-y-4">
          {errorMessage && (
            <div className="flex items-start gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive animate-in fade-in-50 duration-200">
              <AlertCircle className="h-5 w-5 shrink-0 mt-0.5" />
              <div className="leading-snug">{errorMessage}</div>
            </div>
          )}

          <div className="space-y-2">
            {/* Block wrapper: Tailwind v4's space-y margin has no effect on an inline label */}
            <div>
              <Label htmlFor="email" className="text-sm font-medium">
                Email Address
              </Label>
            </div>
            <div className="relative">
              <Mail className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="email"
                type="email"
                placeholder="name@example.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoComplete="email"
                autoFocus={!lastEmail}
                className="pl-9 bg-background/60 focus-visible:ring-primary"
                disabled={isSubmitting}
              />
            </div>
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label htmlFor="password" className="text-sm font-medium">
                Password
              </Label>
            </div>
            <div className="relative">
              <Lock className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="password"
                type="password"
                autoFocus={Boolean(lastEmail)}
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
                className="pl-9 bg-background/60 focus-visible:ring-primary"
                disabled={isSubmitting}
              />
            </div>
          </div>

          {needsCode && (
            <div className="space-y-2">
              <div>
                <Label htmlFor="code" className="text-sm font-medium">
                  Authentication code
                </Label>
              </div>
              <div className="relative">
                <ShieldCheck className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  id="code"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  autoFocus
                  placeholder="123456"
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  className="pl-9 bg-background/60 focus-visible:ring-primary tracking-widest"
                  disabled={isSubmitting}
                  aria-describedby="code-hint"
                />
              </div>
              <p id="code-hint" className="text-xs text-muted-foreground">
                The 6-digit code from your authenticator app, or one of your recovery codes.
              </p>
            </div>
          )}
        </CardContent>

        <CardFooter className="pt-2 pb-8">
          <Button
            type="submit"
            className="w-full font-medium h-10 transition-all duration-200"
            disabled={isSubmitting}
          >
            {isSubmitting ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Signing in...
              </>
            ) : (
              "Sign In"
            )}
          </Button>
        </CardFooter>
      </form>
    </Card>
  );
}
