import { useMemo, useState } from "react";
import { useT } from "../../../i18n";
import { AREA_ORDER, type AnalysisResult, type Area, type ArchitectureSnapshot, type FindingElement, type RuleFinding } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { Skeleton } from "../../ui/Skeleton";
import { Arithmetic } from "./Arithmetic";
import { CostBreakdown } from "./CostBreakdown";
import { Coverage } from "./Coverage";
import { FindingRow, type FindingActions, type IdentifiedFinding } from "./FindingRow";
import { FixReceiptCard } from "./FixReceiptCard";
import { ScoreCard, type Disclosure } from "./ScoreCard";
import { ScoreHistory } from "./ScoreHistory";
import { readCoverage } from "./coverage";
import { drawingCodes, drawingFindings } from "./drawingErrors";
import type { FixReceipt } from "./fixReceipt";
import { groupByProtagonist, type CallLookup } from "./protagonist";
import { scoredAreas } from "./scoreExplain";

export type AnalysisStatus = "idle" | "loading" | "error";

export interface FindingGraph {
  readonly names: ReadonlyMap<string, string>;
  readonly calls: CallLookup;
}

export interface FixControls {
  onApplyFix: (finding: RuleFinding) => void;
  receipt: FixReceipt | null;
  onUndoFix: () => void;
  onDismissFix: () => void;
  onOpenFixCompare: (() => void) | null;
}

export interface HistoryReading {
  entries: ArchitectureSnapshot[];
  olderScoring: number;
}

interface AnalysisPanelProps {
  status: AnalysisStatus;
  result: AnalysisResult | null;
  error: string | null;
  onRetry?: () => void;
  onPreview: (elements: FindingElement[] | null) => void;
  onPin: (findingId: string, elements: FindingElement[]) => void;
  pinnedId: string | null;
  fixes?: FixControls;
  history?: HistoryReading;
  components: number;
  shareToken: string;
  graph: FindingGraph;
  narrow: boolean;
}

type Fold = "arithmetic" | "history" | "cost" | null;

export function identify(findings: RuleFinding[]): IdentifiedFinding[] {
  return findings.map((finding, index) => ({ ...finding, findingId: `${finding.code}#${index}` }));
}

export function AnalysisPanel({ status, result, error, onRetry, onPreview, onPin, pinnedId, fixes, history, components, shareToken, graph, narrow }: AnalysisPanelProps) {
  const t = useT();
  const [fold, setFold] = useState<Fold>(null);
  const [showPassed, setShowPassed] = useState(false);

  const identified = useMemo(() => identify(result?.findings ?? []), [result]);
  const problems = identified.filter((finding) => finding.severity !== "Good");
  const passed = identified.filter((finding) => finding.severity === "Good");
  const drawingFirst = drawingFindings(problems, drawingCodes(result));
  const criticals = problems.filter((finding) => finding.severity === "Critical").length;
  const comparable = history?.entries ?? [];
  const firstScore = comparable.length > 0 ? comparable[0].score : null;

  const actions: FindingActions = {
    onPreview,
    onPin,
    pinnedId,
    onApplyFix: fixes?.onApplyFix,
    fixesBlocked: fixes?.receipt !== null && fixes?.receipt !== undefined && fixes.receipt.after === null && fixes.receipt.before !== null,
  };

  if (status === "error" && !result) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-[13px] font-semibold text-panel-ink">{t("web.engine.error.title")}</p>
        <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("editor.analysis.safe")}</p>
        {error && <p className="font-mono text-[11px] text-panel-ink-3">{error}</p>}
        {onRetry && (
          <Button variant="panel" size="sm" onClick={onRetry}>
            {t("web.engine.retry")}
          </Button>
        )}
      </div>
    );
  }

  if (!result) {
    return (
      <div className="flex flex-col gap-3" aria-busy>
        <Skeleton height={96} dark />
        <Skeleton width="60%" dark />
        <Skeleton height={74} dark />
        <Skeleton width="45%" dark />
        <Skeleton height={74} dark />
      </div>
    );
  }

  const toggle = (next: Fold) => setFold((current) => (current === next ? null : next));
  const disclosures: Disclosure[] = [
    { key: "arithmetic", label: t("ui.analysis.showArithmetic"), open: fold === "arithmetic", onToggle: () => toggle("arithmetic") },
    ...(history ? [{ key: "history", label: t("ui.history.title"), open: fold === "history", onToggle: () => toggle("history") }] : []),
    ...(result.cost && result.cost.components.length > 0 ? [{ key: "cost", label: t("ui.analysis.cost.title"), open: fold === "cost", onToggle: () => toggle("cost") }] : []),
  ];

  const areasWithFindings = AREA_ORDER.filter((area) => problems.some((finding) => finding.area === area));
  const receiptArea = fixes?.receipt?.area ?? null;
  const sections: Area[] = receiptArea && !areasWithFindings.includes(receiptArea) ? [...areasWithFindings, receiptArea].sort((a, b) => AREA_ORDER.indexOf(a) - AREA_ORDER.indexOf(b)) : areasWithFindings;

  return (
    <div className={`flex flex-col gap-[11px] ${status === "loading" ? "opacity-60" : ""}`} aria-busy={status === "loading"}>
      {status === "error" && error && <p className="text-[12px] leading-[1.45] text-hot-panel">{error}</p>}

      {drawingFirst.length > 0 && (
        <div className="rounded-card border border-panel-line-2 bg-panel-3">
          <p className="px-3 pt-3 pb-1 text-[12px] leading-[1.45] text-panel-ink">{t("ui.analysis.drawingFirst", { count: drawingFirst.length })}</p>
          <ul>
            {drawingFirst.map((finding, index) => (
              <FindingRow key={finding.findingId} finding={finding} shareToken={shareToken} narrow={narrow} first={index === 0} {...actions} />
            ))}
          </ul>
        </div>
      )}

      <ScoreCard score={result.score} firstScore={firstScore} problems={problems.length} criticals={criticals} areas={scoredAreas(result)} components={components} disclosures={disclosures} narrow={narrow}>
        {fold === "arithmetic" && <Arithmetic result={result} findings={problems} onPreview={onPreview} onPin={onPin} pinnedId={pinnedId} />}
        {fold === "history" && history && <ScoreHistory history={history.entries} olderScoring={history.olderScoring} />}
        {fold === "cost" && result.cost && <CostBreakdown cost={result.cost} onPreview={onPreview} />}
      </ScoreCard>

      {sections.length > 0 && (
        <div className="rounded-card border border-panel-line bg-panel-2 pb-0.5">
          {sections.map((area, index) => (
            <AreaSection
              key={area}
              area={area}
              first={index === 0}
              findings={problems.filter((finding) => finding.area === area)}
              receipt={fixes?.receipt?.area === area ? fixes : null}
              shareToken={shareToken}
              graph={graph}
              narrow={narrow}
              actions={actions}
            />
          ))}
        </div>
      )}

      {passed.length > 0 && (
        <div className="rounded-card border border-panel-line bg-panel-2">
          <button
            type="button"
            onClick={() => setShowPassed((current) => !current)}
            aria-expanded={showPassed}
            className={`flex w-full items-center gap-2 px-3 py-2 text-left text-[12px] hover:text-panel-ink ${showPassed ? "text-panel-ink" : "text-panel-ink-2"} ${narrow ? "min-h-11" : ""}`}
          >
            <span aria-hidden className="size-2 shrink-0 rounded-full border-2 border-ok-panel" />
            {t("ui.analysis.checksPassed", { count: passed.length })}
          </button>
          {showPassed && (
            <ul className="flex flex-col gap-1.5 border-t border-panel-line px-3 py-2.5">
              {passed.map((finding) => (
                <li key={finding.findingId} className="text-[11px] leading-[1.5] text-panel-ink-2">
                  {t(finding.message)}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <Coverage reading={readCoverage(result)} shareToken={shareToken} narrow={narrow} />
    </div>
  );
}

interface AreaSectionProps {
  area: Area;
  first: boolean;
  findings: IdentifiedFinding[];
  receipt: FixControls | null;
  shareToken: string;
  graph: FindingGraph;
  narrow: boolean;
  actions: FindingActions;
}

function AreaSection({ area, first, findings, receipt, shareToken, graph, narrow, actions }: AreaSectionProps) {
  const t = useT();
  const groups = useMemo(() => groupByProtagonist(findings, graph.calls), [findings, graph.calls]);

  return (
    <div className={first ? "" : "border-t border-panel-line"}>
      <div className={`px-3 ${first ? "pt-3" : "pt-2.5"} pb-2`}>
        <div className="flex items-baseline justify-between gap-2.5">
          <span className="text-[13px] font-semibold text-panel-ink">{t(`ui.area.${area}`)}</span>
          <span className="num shrink-0 text-[11px] text-panel-ink-3">{t("ui.area.findings", { count: findings.length })}</span>
        </div>
        <p className="mt-[3px] text-[12px] leading-[1.45] text-panel-ink-3">{t(`ui.area.question.${area}`)}</p>
      </div>

      {receipt?.receipt && <FixReceiptCard receipt={receipt.receipt} onUndo={receipt.onUndoFix} onDismiss={receipt.onDismissFix} onOpenCompare={receipt.onOpenFixCompare} narrow={narrow} />}

      <ul>
        {groups.flatMap((group, index) => {
          const rows = group.findings.map((finding, position) => (
            <FindingRow key={finding.findingId} finding={finding} shareToken={shareToken} narrow={narrow} first={position === 0 && (index === 0 || group.protagonist !== null)} {...actions} />
          ));
          if (group.protagonist === null) return rows;
          return [
            <li key={`group-${index}`} className="flex items-center gap-2 px-3 pt-2 pb-0.5">
              <span className="eyebrow text-panel-ink-3">{t("ui.analysis.about", { name: graph.names.get(group.protagonist) ?? group.protagonist, count: group.findings.length })}</span>
              <span className="h-px flex-1 bg-panel-line" />
            </li>,
            ...rows,
          ];
        })}
      </ul>
    </div>
  );
}
