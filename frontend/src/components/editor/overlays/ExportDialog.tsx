import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError, analyzeArchitecture } from "../../../api/architectureClient";
import { fitLabel, healthOf } from "../../../export/diagram";
import { useEscape } from "../../../hooks/useEscape";
import { exportFileName } from "../../../export/fileName";
import type { EmbeddedFace } from "../../../export/fonts";
import { composeMarkdown } from "../../../export/markdownExporter";
import { Postcard, POSTCARD_H, POSTCARD_W, postcardFaceTexts, postcardTexts } from "../../../export/Postcard";
import { postcardModel } from "../../../export/postcard";
import { downloadBlob, rasterize, serializeSvg } from "../../../export/rasterize";
import { useT } from "../../../i18n";
import type { AnalysisResult, ArchitectureDto, Fault, SimulationResult } from "../../../types/architecture";
import { frameAtLeast, layoutOf } from "../../engine/layout";
import { Button } from "../../ui/Button";
import { CloseIcon } from "../../ui/Icons";
import { Segmented } from "../../ui/Segmented";
import { Skeleton } from "../../ui/Skeleton";
import { drawingHeight, Thumbnail } from "../starters/Thumbnail";

export interface ExportDialogProps {
  architecture: ArchitectureDto;
  baseline?: ArchitectureDto | null;
  analysis: AnalysisResult | null;
  simulation?: SimulationResult | null;
  faults?: Fault[];
  narrow: boolean;
  onClose: () => void;
}

type Status = "loading" | "ready" | "error";
type View = "document" | "postcard";

const PREVIEW = { width: 688, height: 180 };

export function ExportDialog({ architecture, baseline, analysis, simulation, faults, narrow, onClose }: ExportDialogProps) {
  const t = useT();
  const box = useRef<HTMLDivElement>(null);
  const postcard = useRef<SVGSVGElement | null>(null);
  const copiedTimer = useRef<number | undefined>(undefined);
  const [status, setStatus] = useState<Status>(analysis ? "ready" : "loading");
  const [result, setResult] = useState<AnalysisResult | null>(analysis);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [view, setView] = useState<View>("document");
  const [copied, setCopied] = useState(false);
  const [imageError, setImageError] = useState<string | null>(null);
  const [fonts, setFonts] = useState<EmbeddedFace[] | null>(null);
  const exportedAt = useMemo(() => new Date(), []);

  useEffect(() => {
    if (analysis === null) return;
    setResult(analysis);
    setStatus("ready");
  }, [analysis]);

  useEffect(() => {
    if (analysis !== null) return;
    const controller = new AbortController();
    setStatus("loading");
    setError(null);
    analyzeArchitecture(architecture.shareToken, { signal: controller.signal })
      .then((analyzed) => {
        if (controller.signal.aborted) return;
        setResult(analyzed);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.export.failed"));
        setStatus("error");
      });
    return () => controller.abort();
  }, [analysis, architecture.shareToken, attempt, t]);

  useEffect(() => {
    box.current?.querySelector<HTMLElement>("button")?.focus();
  }, []);

  useEscape(onClose);

  useEffect(() => () => window.clearTimeout(copiedTimer.current), []);

  const document = useMemo(
    () => (result === null ? "" : composeMarkdown({ architecture, baseline, analysis: result, exportedAt, t, simulation, faults })),
    [architecture, baseline, result, exportedAt, t, simulation, faults],
  );
  const model = useMemo(() => (result === null ? null : postcardModel({ architecture, analysis: result, origin: window.location.origin })), [architecture, result]);
  const health = useMemo(() => healthOf(simulation), [simulation]);
  const frame = useMemo(() => frameAtLeast(layoutOf({ nodes: architecture.nodes }, { nodeHeight: drawingHeight(simulation ? health : null) }), PREVIEW.width, PREVIEW.height), [architecture.nodes, simulation, health]);
  const faceTexts = useMemo(() => (model === null ? null : postcardFaceTexts(postcardTexts(model, t), architecture.nodes)), [model, t, architecture.nodes]);

  useEffect(() => {
    if (view !== "postcard" || faceTexts === null) return;
    let live = true;
    import("../../../export/embedFonts")
      .then(({ embedPostcardFonts }) => embedPostcardFonts(faceTexts))
      .then((faces) => {
        if (live) setFonts(faces);
      })
      .catch(() => {
        if (live) setFonts([]);
      });
    return () => {
      live = false;
    };
  }, [view, faceTexts]);

  const copy = async () => {
    await navigator.clipboard.writeText(document);
    setCopied(true);
    window.clearTimeout(copiedTimer.current);
    copiedTimer.current = window.setTimeout(() => setCopied(false), 2000);
  };
  const downloadMarkdown = () => downloadBlob(new Blob([document], { type: "text/markdown" }), exportFileName(architecture, "md"));
  const downloadSvg = () => {
    if (postcard.current === null) return;
    setImageError(null);
    downloadBlob(new Blob([serializeSvg(postcard.current)], { type: "image/svg+xml" }), exportFileName(architecture, "svg"));
  };
  const downloadPng = async () => {
    if (postcard.current === null) return;
    setImageError(null);
    try {
      downloadBlob(await rasterize(serializeSvg(postcard.current), POSTCARD_W, POSTCARD_H), exportFileName(architecture, "png"));
    } catch {
      setImageError(t("ui.export.postcard.failed"));
    }
  };

  const ready = status === "ready" && result !== null;
  const button = narrow ? "min-h-11 flex-1" : "";

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="editor-export-title" className="fixed inset-0 z-50 flex items-center justify-center bg-canvas/80 p-3 md:p-6" onClick={onClose}>
      <div ref={box} className="flex max-h-full w-full max-w-[760px] flex-col gap-3 overflow-hidden rounded-panel border border-panel-line-2 bg-panel p-4 text-panel-ink shadow-[0_30px_60px_-30px_rgba(0,0,0,.8)]" onClick={(event) => event.stopPropagation()}>
        <div className="flex flex-wrap items-center gap-3">
          <h2 id="editor-export-title" className="font-display text-[17px] leading-[1.2] font-semibold text-panel-ink">
            {t("ui.export.title")}
          </h2>
          <div className="ml-auto flex items-center gap-2">
            <Segmented<View>
              options={[
                { value: "document", label: t("ui.export.view.document") },
                { value: "postcard", label: t("ui.export.view.postcard") },
              ]}
              value={view}
              onChange={setView}
              label={t("ui.export.title")}
              dark
            />
            <button type="button" onClick={onClose} aria-label={t("ui.close")} className={`inline-flex shrink-0 items-center justify-center rounded-pill text-panel-ink-2 hover:text-panel-ink ${narrow ? "size-11" : "size-9"}`}>
              <CloseIcon size={18} />
            </button>
          </div>
        </div>
        <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("ui.export.lead")}</p>

        <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden">
          {status === "loading" && (
            <div className="flex min-h-0 flex-1 flex-col gap-3" aria-busy>
              <Skeleton height={PREVIEW.height} dark />
              <Skeleton height={160} dark />
            </div>
          )}
          {status === "error" && (
            <div className="flex flex-1 flex-col items-center justify-center gap-2 py-10 text-center">
              <p className="text-[13px] font-semibold text-panel-ink">{error ?? t("ui.export.failed")}</p>
              <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("editor.export.safe")}</p>
            </div>
          )}
          {ready && view === "document" && (
            <>
              <Thumbnail nodes={architecture.nodes} connections={architecture.connections} frame={frame} health={simulation ? health : null} className={`shrink-0 rounded-card ${narrow ? "h-[140px]" : "h-[180px]"}`} />
              <pre className="min-h-0 flex-1 overflow-auto rounded-card border border-panel-line bg-panel-2 p-3.5 font-mono text-[12px] leading-[1.5] break-words whitespace-pre-wrap text-panel-ink-2" tabIndex={0}>
                {document}
              </pre>
            </>
          )}
          {ready && view === "postcard" && model !== null && (
            <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 overflow-auto">
              <Postcard svgRef={postcard} model={model} nodes={architecture.nodes} connections={architecture.connections} t={t} fonts={fonts} className="block h-auto w-full max-w-[1200px] rounded-panel border border-panel-line" />
              {imageError !== null && <p className="text-[12px] leading-[1.45] text-hot-panel">{imageError}</p>}
            </div>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2.5 border-t border-panel-line pt-3">
          {status === "error" ? (
            <Button variant="panel" size="sm" onClick={() => setAttempt((n) => n + 1)} className={button}>
              {t("ui.editor.retry")}
            </Button>
          ) : view === "document" ? (
            <>
              <Button variant="panel" size="sm" onClick={copy} disabled={!ready} className={button}>
                {t(copied ? "ui.copied" : "editor.export.copy")}
              </Button>
              <Button variant="panelPrimary" size="sm" onClick={downloadMarkdown} disabled={!ready} loading={status === "loading"} loadingLabel={t("ui.export.composing")} className={button}>
                {t("ui.export.download")}
              </Button>
            </>
          ) : (
            <>
              <Button variant="panelPrimary" size="sm" onClick={downloadPng} disabled={!ready} className={button}>
                {t("ui.export.postcard.png")}
              </Button>
              <Button variant="panel" size="sm" onClick={downloadSvg} disabled={!ready} className={button}>
                {t("ui.export.postcard.svg")}
              </Button>
            </>
          )}
          {status !== "error" && <span className={`text-[11px] leading-[1.4] text-panel-ink-3 ${narrow ? "w-full" : "ml-auto text-right"}`}>{fitLabel(t(view === "document" ? "ui.export.hintRead" : "ui.export.postcard.lead"), 480, 11)}</span>}
        </div>
      </div>
    </div>
  );
}
