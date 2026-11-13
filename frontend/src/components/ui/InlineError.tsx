export function InlineError({ children }: { children: string }) {
  return (
    <p role="alert" className="flex items-start gap-2 text-small text-hot">
      <span aria-hidden className="mt-[7px] size-2 shrink-0 rounded-full bg-hot" />
      {children}
    </p>
  );
}
