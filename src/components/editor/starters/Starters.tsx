import { useMemo } from "react";
import { useT } from "../../../i18n";
import { Chip } from "../../ui/Chip";
import { STARTER_ARCHITECTURES, starterToDto, type StarterArchitecture } from "./starterArchitectures";
import { Thumbnail } from "./Thumbnail";

export function Starters({ onPick }: { onPick: (starter: StarterArchitecture) => void }) {
  const t = useT();
  const drawings = useMemo(() => STARTER_ARCHITECTURES.map((starter) => ({ starter, ...starterToDto(starter) })), []);

  return (
    <div className="pointer-events-auto flex max-w-[760px] flex-col items-center gap-3">
      <span className="eyebrow text-panel-ink-3">{t("ui.starter.heading")}</span>
      <div className="flex flex-wrap justify-center gap-3">
        {drawings.map(({ starter, nodes, connections }) => (
          <button
            key={starter.slug}
            type="button"
            onClick={() => onPick(starter)}
            title={t("ui.starter.score", { score: starter.score })}
            className="flex w-[172px] flex-col rounded-card border border-panel-line bg-panel-2 p-2 text-left transition-colors hover:border-panel-line-2 hover:bg-panel-3"
          >
            <Thumbnail nodes={nodes} connections={connections} />
            <span className="mt-2 block min-h-[34px] text-[13px] leading-tight font-semibold text-panel-ink">{t(starter.label)}</span>
            <span className="mt-1.5 flex flex-wrap items-center gap-1">
              <Chip variant="panel" className="num">
                {starter.score}
              </Chip>
              {starter.criticals > 0 && (
                <Chip variant="critical" className="num">
                  {t("ui.starter.criticals", { count: starter.criticals })}
                </Chip>
              )}
            </span>
            <span className="mt-1.5 block text-meta leading-snug text-panel-ink-3">{t(starter.lesson)}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
