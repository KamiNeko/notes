# @kamineko/ui

Shared React UI for self-hosted apps: [shadcn/ui](https://ui.shadcn.com) components, a warm light and dark theme as Tailwind CSS 4 tokens, a theme provider and toggle, and small helpers. Notes is the first app that uses it.

The package ships TypeScript sources and CSS; the app's bundler (Vite) compiles them. It lives in the Notes repository as an npm workspace for now. How it can be published to GitHub Packages or moved to its own repository later is described in [ADR-12](../../docs/architecture/09-architecture-decisions.md#adr-12-shared-ui-package-in-the-repository).

## Use

Requirements: React 19, Tailwind CSS 4, `lucide-react` and `sonner` (peer dependencies).

```css
/* the app's main stylesheet */
@import "tailwindcss";
@import "@kamineko/ui/theme.css";
```

```tsx
import { Button } from "@kamineko/ui/components/button";
import { cn } from "@kamineko/ui/lib/utils";
import { ThemeProvider } from "@kamineko/ui/theme/ThemeProvider";
import { ThemeToggle } from "@kamineko/ui/theme/ThemeToggle";

<ThemeProvider defaultTheme="dark" storageKey="my-app-theme">
  <ThemeToggle />
  <Button className={cn("w-full")}>Save</Button>
</ThemeProvider>
```

`theme.css` defines the colours as HSL triples (`--background`, `--primary`, …) in `:root` (light) and `.dark`, maps them to Tailwind colours (`bg-background`, `text-muted-foreground`, …) and tells Tailwind to scan this package's components for class names. An app adds its own tokens in its stylesheet, after the import.

| Export | Content |
| :--- | :--- |
| `@kamineko/ui/components/<name>` | avatar, badge, button, card, collapsible, dialog, dropdown-menu, input, label, popover, scroll-area, separator, sheet, sidebar, skeleton, sonner (`Toaster`), tooltip |
| `@kamineko/ui/theme/ThemeProvider`, `…/ThemeToggle` | Light, dark or system theme on `<html>`, remembered in `localStorage` under `storageKey` |
| `@kamineko/ui/hooks/use-theme` | `useTheme()`: `{ theme, setTheme, isDark }` |
| `@kamineko/ui/hooks/use-mobile` | `useIsMobile(breakpoint = 768)` |
| `@kamineko/ui/lib/utils` | `cn(...)`: `clsx` + `tailwind-merge` |
| `@kamineko/ui/theme.css` | Tokens, Tailwind mapping, base styles, scrollbars |

## Rules

- Nothing app-specific: no imports from an app (`@/…`), no API, routing or query code. Imports inside the package are relative.
- New shadcn components: `npx shadcn@latest add <name>` in an app with the usual `@/` aliases, then move the file to `src/components/` and change its imports to relative ones (`../lib/utils`, `./button`). `components.json` documents the style settings.
- Both themes and phone width must work; JSDoc on exported members.

```bash
npm --prefix packages/ui run lint
npm --prefix packages/ui run typecheck
npm --prefix packages/ui test
```
