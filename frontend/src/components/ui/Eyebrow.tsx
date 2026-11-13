export function Eyebrow({ children, accent = false }: { children: string; accent?: boolean }) {
  return <span className={`eyebrow ${accent ? "eyebrow-accent" : ""}`}>{children}</span>;
}
