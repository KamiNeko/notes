import { useStorage } from "@/hooks/use-storage";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * The space the signed-in user's attachments take, with a bar when there is a limit. Shown in the
 * account menu and only fetched while it is open.
 */
export function StorageMeter({ enabled }: { enabled: boolean }) {
  const { data } = useStorage(enabled);
  if (!data) return null;
  const { usedBytes, quotaBytes } = data;
  const percent = quotaBytes ? Math.min(100, Math.round((usedBytes / quotaBytes) * 100)) : 0;
  const label = quotaBytes
    ? `${formatBytes(usedBytes)} of ${formatBytes(quotaBytes)} used`
    : `${formatBytes(usedBytes)} used`;
  return (
    <div className="space-y-1.5 pt-1">
      <p className="text-[11px] leading-none text-muted-foreground">Attachments: {label}</p>
      {quotaBytes !== null && (
        <div
          role="progressbar"
          aria-label="Storage used"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent}
          aria-valuetext={label}
          className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
        >
          <div
            className={cn("h-full rounded-full", percent >= 90 ? "bg-destructive" : "bg-primary")}
            style={{ width: `${percent}%` }}
          />
        </div>
      )}
    </div>
  );
}
