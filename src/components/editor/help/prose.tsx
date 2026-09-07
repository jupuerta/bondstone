import { Fragment } from "react";

const RULE_CODE = /\b(?:[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+|SPOF|CYCLE)\b/g;

export function RuleProse({ text, className }: { text: string; className?: string }) {
  return <p className={className}>{mark(text)}</p>;
}

export function ruleCodesIn(text: string): string[] {
  return text.match(RULE_CODE) ?? [];
}

function mark(text: string) {
  const pieces: React.ReactNode[] = [];
  let cursor = 0;

  for (const match of text.matchAll(RULE_CODE)) {
    const at = match.index;
    if (at > cursor) pieces.push(<Fragment key={cursor}>{text.slice(cursor, at)}</Fragment>);

    pieces.push(
      <span key={at} className="font-mono text-[11px] tracking-[0.02em] text-panel-ink">
        {match[0]}
      </span>,
    );

    cursor = at + match[0].length;
  }

  if (cursor < text.length) pieces.push(<Fragment key={cursor}>{text.slice(cursor)}</Fragment>);

  return pieces;
}
