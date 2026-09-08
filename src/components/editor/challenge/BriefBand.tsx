import { useState } from "react";
import type { EvaluationStatus } from "../../../hooks/useChallenge";
import { useT } from "../../../i18n";
import type { ChallengeDetail, ChallengeRequirementInfo, ChallengeRequirementResult, ChallengeResult, FindingElement, LocalizedText } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { Chip } from "../../ui/Chip";
import { lessonKey } from "./journal";

export interface RequirementActions {
  onPreview: (elements: FindingElement[] | null) => void;
  onPin: (id: string, elements: FindingElement[]) => void;
  pinnedId: string | null;
}

interface BriefBandProps {
  definition: ChallengeDetail;
  result: ChallengeResult | null;
  status: EvaluationStatus;
  error: string | null;
  collapsed: boolean;
  onToggle: () => void;
  onEvaluate: () => void;
  own: boolean;
  narrow: boolean;
  actions: RequirementActions;
}

const PINNED_TINT = "color-mix(in oklab, var(--af-warm-panel) 7%, transparent)";
const PINNED_LINE = "color-mix(in oklab, var(--af-warm-panel) 40%, transparent)";

export function BriefBand({ definition, result, status, error, collapsed, onToggle, onEvaluate, own, narrow, actions }: BriefBandProps) {
  const t = useT();
  const met = result?.requirements.filter((requirement) => requirement.met).length ?? 0;
  const total = definition.requirements.length;
  const toggleLabel = t(collapsed ? "ui.challenge.expand" : "ui.challenge.collapse");
  const verdict = <Verdict result={result} status={status} met={met} total={total} onEvaluate={onEvaluate} narrow={narrow} />;

  if (narrow) {
    return (
      <section aria-label={t("ui.challenge.brief")} className="shrink-0 border-b border-panel-line bg-panel-2">
        <div className="flex items-center gap-2 px-3 py-1">
          <button type="button" onClick={onToggle} aria-expanded={!collapsed} aria-label={toggleLabel} className="flex min-h-11 min-w-0 flex-1 items-center gap-2 rounded-node text-left">
            <span className="min-w-0 truncate font-display text-[15px] leading-[1.2] font-semibold text-panel-ink">{t(definition.title)}</span>
            <Chevron up={!collapsed} />
          </button>
          {verdict}
        </div>
        {!collapsed && (
          <div className="flex flex-col gap-3 border-t border-panel-line px-3 py-3">
            <Chip variant="panel" className="self-start">
              {t(`ui.difficulty.${definition.difficulty}`)}
            </Chip>
            <Story story={definition.story} />
            <Lesson slug={definition.slug} />
            <Checks definition={definition} result={result} actions={actions} narrow />
            <Hints hints={definition.hints} narrow />
            <Notes own={own} error={error} />
          </div>
        )}
      </section>
    );
  }

  return (
    <section aria-label={t("ui.challenge.brief")} className="shrink-0 border-b border-panel-line bg-panel-2 px-4 py-3.5">
      <div className="flex items-center gap-4">
        <div className="flex min-w-0 flex-1 items-center gap-2.5">
          <h1 className="min-w-0 truncate font-display text-[19px] leading-[1.2] font-semibold text-panel-ink">{t(definition.title)}</h1>
          <Chip variant="panel" className="shrink-0">
            {t(`ui.difficulty.${definition.difficulty}`)}
          </Chip>
        </div>
        {collapsed && verdict}
        <button type="button" onClick={onToggle} aria-expanded={!collapsed} className="inline-flex min-h-8 shrink-0 items-center gap-1.5 rounded-pill px-2 text-[12px] text-panel-ink-2 hover:text-panel-ink">
          {toggleLabel}
          <Chevron up={!collapsed} />
        </button>
      </div>
      {!collapsed && (
        <div className="mt-2.5 grid grid-cols-[1.5fr_1.2fr_1fr] items-start gap-7">
          <div className="flex flex-col gap-2.5">
            <Story story={definition.story} />
            <Lesson slug={definition.slug} />
          </div>
          <Checks definition={definition} result={result} actions={actions} narrow={false} />
          <div className="flex flex-col gap-2.5">
            <Hints hints={definition.hints} narrow={false} />
            {verdict}
            <Notes own={own} error={error} />
          </div>
        </div>
      )}
    </section>
  );
}

interface VerdictProps {
  result: ChallengeResult | null;
  status: EvaluationStatus;
  met: number;
  total: number;
  onEvaluate: () => void;
  narrow: boolean;
}

function Verdict({ result, status, met, total, onEvaluate, narrow }: VerdictProps) {
  const t = useT();
  const marker = result ? (
    <span className="num shrink-0 text-[15px] font-semibold text-panel-ink">{t("editor.challenge.progress", { met, total })}</span>
  ) : (
    <span className="num shrink-0 text-[12px] text-panel-ink-3">{t("ui.challenge.requirementCount", { count: total })}</span>
  );
  const action = result?.passed ? (
    <span className="inline-flex shrink-0 items-center gap-1.5 text-[13px] font-semibold text-ok-panel">
      <CheckMark size={14} className="shrink-0" />
      {t("ui.challenge.passed")}
    </span>
  ) : (
    <Button variant="panelPrimary" size="sm" onClick={onEvaluate} loading={status === "evaluating"} loadingLabel={t("ui.challenge.evaluating")} className={narrow ? "min-h-11" : ""}>
      {t("ui.challenge.evaluate")}
    </Button>
  );

  return (
    <div className="flex shrink-0 items-center gap-3">
      {narrow ? marker : action}
      {narrow ? action : marker}
    </div>
  );
}

function Story({ story }: { story: LocalizedText }) {
  const t = useT();
  return (
    <div className="flex flex-col gap-2">
      {t(story)
        .split(/\n{2,}/)
        .map((paragraph, index) => (
          <p key={index} className="text-[12px] leading-[1.5] whitespace-pre-line text-panel-ink-2">
            {paragraph}
          </p>
        ))}
    </div>
  );
}

function Lesson({ slug }: { slug: string }) {
  const t = useT();
  return (
    <div className="border-l-2 border-panel-ink pl-[11px]">
      <span className="eyebrow text-panel-ink-3">{t("web.challenge.teaches")}</span>
      <p className="mt-[3px] text-[12px] leading-[1.45] text-panel-ink">{t(lessonKey(slug))}</p>
    </div>
  );
}

interface ChecksProps {
  definition: ChallengeDetail;
  result: ChallengeResult | null;
  actions: RequirementActions;
  narrow: boolean;
}

function Checks({ definition, result, actions, narrow }: ChecksProps) {
  const t = useT();
  const pointable = [...(result?.requirements ?? []), ...(result?.optionalGoals ?? [])].some((outcome) => !outcome.met && outcome.elements.length > 0);

  return (
    <div className="flex flex-col gap-1.5">
      <span className="eyebrow text-panel-ink-3">{t("ui.challenge.checks")}</span>
      <ul className="flex flex-col gap-[7px] rounded-card border border-panel-line bg-panel p-2.5">
        {definition.requirements.map((info, index) => (
          <Requirement key={index} id={`requirement#${index}`} info={info} outcome={result?.requirements[index] ?? null} actions={actions} narrow={narrow} />
        ))}
      </ul>
      {definition.optionalGoals.length > 0 && (
        <>
          <span className="eyebrow mt-1 text-panel-ink-3">{t("ui.challenge.extra")}</span>
          <ul className="flex flex-col gap-[7px] px-2.5">
            {definition.optionalGoals.map((info, index) => (
              <Requirement key={index} id={`goal#${index}`} info={info} outcome={result?.optionalGoals[index] ?? null} actions={actions} narrow={narrow} optional />
            ))}
          </ul>
        </>
      )}
      {pointable && <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("editor.challenge.highlightHint")}</p>}
    </div>
  );
}

interface RequirementProps {
  id: string;
  info: ChallengeRequirementInfo;
  outcome: ChallengeRequirementResult | null;
  actions: RequirementActions;
  narrow: boolean;
  optional?: boolean;
}

function Requirement({ id, info, outcome, actions, narrow, optional = false }: RequirementProps) {
  const t = useT();
  const elements = outcome?.elements ?? [];
  const interactive = outcome !== null && !outcome.met && elements.length > 0;
  const pinned = interactive && actions.pinnedId === id;
  const inner = "flex w-full flex-col gap-1 px-1 py-0.5 text-left";

  const body = (
    <>
      <span className="flex items-start gap-2">
        <Mark outcome={outcome} optional={optional} />
        <span className={`min-w-0 flex-1 text-[12px] leading-[1.45] ${outcome?.met ? "text-panel-ink-2" : optional && outcome === null ? "text-panel-ink-3" : "text-panel-ink"}`}>{t(info.label)}</span>
        {outcome && (
          <span className={`shrink-0 font-mono text-[11px] leading-[1.45] tracking-[0.06em] uppercase ${outcome.met ? "text-ok-panel" : "text-panel-ink-3"}`}>{t(outcome.met ? "editor.challenge.met" : "editor.challenge.unmet")}</span>
        )}
      </span>
      {outcome && !outcome.met && outcome.detail && <span className="ml-[22px] block text-[11px] leading-[1.45] text-panel-ink-2">{t(outcome.detail)}</span>}
    </>
  );

  return (
    <li className="-mx-1 rounded-node" style={pinned ? { background: PINNED_TINT, boxShadow: `inset 0 0 0 1px ${PINNED_LINE}` } : undefined}>
      {interactive ? (
        <button
          type="button"
          aria-pressed={pinned}
          onMouseEnter={() => actions.onPreview(elements)}
          onMouseLeave={() => actions.onPreview(null)}
          onFocus={() => actions.onPreview(elements)}
          onBlur={() => actions.onPreview(null)}
          onClick={() => actions.onPin(id, elements)}
          className={`${inner} cursor-pointer ${narrow ? "min-h-11 justify-center" : ""}`}
        >
          {body}
        </button>
      ) : (
        <div className={inner}>{body}</div>
      )}
    </li>
  );
}

function Mark({ outcome, optional }: { outcome: ChallengeRequirementResult | null; optional: boolean }) {
  if (outcome?.met) return <CheckMark size={14} className="mt-[3px] shrink-0 text-ok-panel" />;
  return (
    <svg width={14} height={14} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.4} aria-hidden className={`mt-[3px] shrink-0 text-panel-ink-3 ${optional ? "opacity-70" : ""}`}>
      <circle cx="12" cy="12" r="8" />
    </svg>
  );
}

function Hints({ hints, narrow }: { hints: LocalizedText[]; narrow: boolean }) {
  const t = useT();
  const [shown, setShown] = useState(0);
  if (hints.length === 0) return null;
  const link = `self-start text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink ${narrow ? "min-h-11" : ""}`;

  if (shown === 0) {
    return (
      <button type="button" onClick={() => setShown(1)} className={link}>
        {t("ui.challenge.needHint")}
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-1.5 rounded-card border border-dashed border-panel-line-2 p-2.5">
      {hints.slice(0, shown).map((hint, index) => (
        <p key={index} className="text-[12px] leading-[1.45] text-panel-ink-2">
          {t(hint)}
        </p>
      ))}
      {shown < hints.length && (
        <button type="button" onClick={() => setShown((value) => value + 1)} className={link}>
          {t("ui.challenge.hintsLeft", { remaining: hints.length - shown })}
        </button>
      )}
    </div>
  );
}

function Notes({ own, error }: { own: boolean; error: string | null }) {
  const t = useT();
  return (
    <>
      <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t(own ? "editor.challenge.evaluationNote" : "editor.challenge.visitorNote")}</p>
      {error && <p className="text-[12px] leading-[1.45] text-hot-panel">{error}</p>}
    </>
  );
}

function Chevron({ up }: { up: boolean }) {
  return (
    <svg width="10" height="10" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden className="shrink-0">
      {up ? <path d="M2 7.5l4-4 4 4" /> : <path d="M2 4.5l4 4 4-4" />}
    </svg>
  );
}

export function CheckMark({ size = 14, className = "" }: { size?: number; className?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round" aria-hidden className={className}>
      <path d="M4 12.5l5 5L20 6.5" />
    </svg>
  );
}
