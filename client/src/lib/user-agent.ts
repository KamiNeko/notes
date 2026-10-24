const BROWSERS: [RegExp, string][] = [
  [/Edg(A|iOS)?\//, "Edge"],
  [/OPR\/|Opera/, "Opera"],
  [/Firefox\/|FxiOS\//, "Firefox"],
  [/Chrome\/|CriOS\//, "Chrome"],
  [/Safari\//, "Safari"],
];

const SYSTEMS: [RegExp, string][] = [
  [/iPhone/, "iPhone"],
  [/iPad/, "iPad"],
  [/Android/, "Android"],
  [/Windows/, "Windows"],
  [/Mac OS X|Macintosh/, "macOS"],
  [/CrOS/, "ChromeOS"],
  [/Linux/, "Linux"],
];

/**
 * A short label for a User-Agent, such as "Firefox on Linux", for the session list. Falls back to
 * the start of the string for API clients and to "Unknown device" when there is none.
 */
export function describeUserAgent(userAgent: string): string {
  const ua = userAgent.trim();
  if (!ua) return "Unknown device";
  const browser = BROWSERS.find(([pattern]) => pattern.test(ua))?.[1];
  const system = SYSTEMS.find(([pattern]) => pattern.test(ua))?.[1];
  if (browser && system) return `${browser} on ${system}`;
  if (browser || system) return (browser ?? system)!;
  return ua.length > 40 ? `${ua.slice(0, 40)}…` : ua;
}
