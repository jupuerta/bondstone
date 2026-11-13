export function Skeleton({ width = "100%", height = 14, dark = false }: { width?: string | number; height?: number; dark?: boolean }) {
  return (
    <span
      aria-hidden
      className={`block animate-pulse rounded ${dark ? "bg-wash/10" : "bg-line"}`}
      style={{ width, height }}
    />
  );
}
