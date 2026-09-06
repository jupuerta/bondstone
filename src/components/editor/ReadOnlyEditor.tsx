import { useCallback, useEffect, useMemo, useState } from "react";
import { getChallenge } from "../../api/challengeClient";
import { scenarioFromSearch } from "../../domain/scenarioUrl";
import { useNarrowViewport } from "../../hooks/useMediaQuery";
import { usePageTitle } from "../../hooks/usePageTitle";
import { useT } from "../../i18n";
import type { ChallengeDetail } from "../../types/architecture";
import { useEngine, type EngineData } from "../engine/useEngine";
import { Button } from "../ui/Button";
import { Canvas } from "./canvas/Canvas";
import { isEntryPoint } from "./canvas/entryPoint";
import type { Selection } from "./canvas/types";
import { EdgeInspector } from "./inspector/EdgeInspector";
import { NodeInspector } from "./inspector/NodeInspector";
import { EditorLoading } from "./overlays/EditorLoading";
import { EditorNotFound } from "./overlays/EditorNotFound";
import { BottomDrawer } from "./shell/BottomDrawer";
import { EditorBar, type BarChip } from "./shell/EditorBar";
import { MotorSummary } from "./shell/MotorSummary";
import { NarrowMenu } from "./shell/NarrowMenu";
import { useShareLink } from "./shell/useShareLink";
import { elementOf, endsOf, titleOf } from "./selection";

interface ReadOnlyEditorProps {
  shareToken: string;
  search: string;
  onOpenCopy: () => void;
  copying: boolean;
  copyError: string | null;
}

export function ReadOnlyEditor({ shareToken, search, onOpenCopy, copying, copyError }: ReadOnlyEditorProps) {
  const t = useT();
  const faults = useMemo(() => scenarioFromSearch(search), [search]);
  const { state, retry } = useEngine(shareToken, faults);
  const narrow = useNarrowViewport();
  const [selection, setSelection] = useState<Selection | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const challengeSlug = useMemo(() => new URLSearchParams(search).get("challenge"), [search]);
  const [challenge, setChallenge] = useState<ChallengeDetail | null>(null);
  const { copy, copied } = useShareLink(`${window.location.origin}/a/${encodeURIComponent(shareToken)}${search}`);
  usePageTitle(state.status === "ready" ? state.data.architecture.name : t("web.engine.readOnly"));

  useEffect(() => {
    setChallenge(null);
    if (!challengeSlug) return;
    let cancelled = false;
    getChallenge(challengeSlug)
      .then((detail) => {
        if (!cancelled) setChallenge(detail);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [challengeSlug]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setSelection(null);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const select = useCallback((next: Selection | null) => {
    setSelection(next);
    if (next) setDrawerOpen(true);
  }, []);

  const closeMenu = useCallback(() => setMenuOpen(false), []);

  if (state.status === "loading") return <EditorLoading />;
  if (state.status === "error") return <EditorNotFound message={state.message} onRetry={retry} />;

  const { architecture, analysis, simulation } = state.data;
  const faultIds = new Set(state.data.faults.flatMap((fault) => ("id" in fault ? [fault.id] : [])));
  const chips = chipsOf(state.data, challenge, t);
  const selected = selection ? elementOf(architecture, selection) : null;
  const openCopy = (size: "sm" | "md") => (
    <Button variant="panelPrimary" size={size} onClick={onOpenCopy} loading={copying} loadingLabel={t("ui.editor.openingCopy")} className={size === "md" ? "w-full" : undefined}>
      {t("ui.editor.openCopy")}
    </Button>
  );
  const shell = (
    <EditorBar
      name={architecture.name}
      shareToken={shareToken}
      chips={chips}
      narrow={narrow}
      primary={openCopy("sm")}
      notice={copyError}
      onShare={copy}
      shared={copied}
      onOpenMenu={() => setMenuOpen(true)}
      menuOpen={menuOpen}
    />
  );
  const canvas = (
    <Canvas
      architecture={architecture}
      states={simulation?.states ?? null}
      faultIds={faultIds}
      callLoad={analysis.callLoad ?? null}
      selection={selection}
      onSelect={select}
      narrow={narrow}
    />
  );
  const inspector = selected && (
    <div className="flex flex-col gap-3">
      {selected.kind === "node" ? (
        <NodeInspector node={selected.node} isEntryPoint={isEntryPoint(selected.node.id, architecture.nodes, architecture.connections)} heading={narrow} />
      ) : (
        <EdgeInspector connection={selected.connection} {...endsOf(architecture, selected.connection)} heading={narrow} />
      )}
    </div>
  );

  if (narrow) {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        {shell}
        {canvas}
        <BottomDrawer
          title={selected ? titleOf(selected, architecture, t) : `${t("ui.analysis.title")} · ${analysis.score ?? "—"}`}
          open={drawerOpen}
          onToggle={() => setDrawerOpen((open) => !open)}
          aside={
            selected ? (
              <Button variant="panel" size="sm" onClick={() => setSelection(null)} className="min-h-11">
                {t("editor.selection.back")}
              </Button>
            ) : undefined
          }
        >
          {inspector ?? <MotorSummary analysis={analysis} shareToken={shareToken} faultIds={faultIds} />}
        </BottomDrawer>
        {menuOpen && <NarrowMenu shareToken={shareToken} onClose={closeMenu} primary={openCopy("md")} onShare={copy} shared={copied} />}
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {shell}
      <div className="flex min-h-0 flex-1">
        {inspector && (
          <aside className="flex w-[264px] shrink-0 flex-col border-r border-panel-line bg-panel">
            <div className="flex h-11 items-center justify-between border-b border-panel-line px-3">
              <span className="eyebrow text-panel-ink-3">{selected?.kind === "node" ? t("editor.inspector.component") : t("ui.inspector.call")}</span>
              <button type="button" onClick={() => setSelection(null)} className="rounded-pill px-2 py-1 text-meta text-panel-ink-2 hover:text-panel-ink">
                {t("ui.close")}
              </button>
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto p-3">{inspector}</div>
          </aside>
        )}
        {canvas}
        <aside className="flex w-[336px] shrink-0 flex-col border-l border-panel-line bg-panel">
          <div className="flex h-11 items-center border-b border-panel-line px-3">
            <h2 className="font-display text-panel font-semibold text-panel-ink">{t("ui.analysis.title")}</h2>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3">
            <MotorSummary analysis={analysis} shareToken={shareToken} faultIds={faultIds} />
          </div>
        </aside>
      </div>
    </div>
  );
}

function chipsOf(data: EngineData, challenge: ChallengeDetail | null, t: ReturnType<typeof useT>): BarChip[] {
  const chips: BarChip[] = [{ key: "readOnly", label: t("web.engine.readOnly") }];
  if (challenge) chips.push({ key: "challenge", label: `${t("ui.challenge.title")} · ${t(challenge.title)}` });
  for (const fault of data.faults) {
    if (!("id" in fault)) continue;
    const node = data.architecture.nodes.find((candidate) => candidate.id === fault.id);
    if (node) chips.push({ key: `fault-${fault.id}`, label: t("web.engine.fault", { name: node.name }), variant: "critical" });
  }
  return chips;
}
