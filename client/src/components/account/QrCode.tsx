import { useMemo } from "react";
import { encode } from "uqr";

/**
 * A QR code drawn as one SVG path, dark on white in both themes so every phone camera can read it.
 */
export function QrCode({ value, label, size = 176 }: { value: string; label: string; size?: number }) {
  const { path, modules } = useMemo(() => {
    const qr = encode(value, { ecc: "M", border: 2 });
    let d = "";
    qr.data.forEach((row, y) =>
      row.forEach((dark, x) => {
        if (dark) d += `M${x} ${y}h1v1h-1z`;
      })
    );
    return { path: d, modules: qr.size };
  }, [value]);

  return (
    <svg role="img" aria-label={label} width={size} height={size} viewBox={`0 0 ${modules} ${modules}`} shapeRendering="crispEdges" className="rounded-md">
      <rect width={modules} height={modules} fill="#ffffff" />
      <path d={path} fill="#000000" />
    </svg>
  );
}
