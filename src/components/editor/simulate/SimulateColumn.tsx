import { useCallback, useState } from "react";
import { SCENARIO_WIDTH_MAX, SCENARIO_WIDTH_MIN, clampScenarioWidth, editorPrefs, rememberEditorPrefs } from "../../../domain/editorPrefs";
import { useT } from "../../../i18n";
import { ColumnHandle } from "../shell/ColumnHandle";
import { FaultPicker, type FaultPickerProps } from "./FaultPicker";
import { Scenario, type ScenarioProps } from "./Scenario";

interface SimulateColumnProps {
  scenario: Omit<ScenarioProps, "narrow">;
  picker: Omit<FaultPickerProps, "narrow"> | null;
}

export function SimulateColumn({ scenario, picker }: SimulateColumnProps) {
  const t = useT();
  const [width, setWidth] = useState(() => editorPrefs().scenarioWidth);
  const resize = useCallback((next: number) => {
    rememberEditorPrefs({ scenarioWidth: next });
    setWidth(next);
  }, []);

  return (
    <aside className="relative flex shrink-0 flex-col border-r border-panel-line bg-panel" style={{ width }}>
      <ColumnHandle side="left" width={width} min={SCENARIO_WIDTH_MIN} max={SCENARIO_WIDTH_MAX} clamp={clampScenarioWidth} label={t("ui.scenario.resize")} onChange={resize} />
      <div className="flex min-h-0 flex-1 flex-col gap-2.5 overflow-y-auto p-3">
        <Scenario {...scenario} narrow={false} />
        {picker && (
          <div className="border-t border-panel-line pt-2.5">
            <FaultPicker {...picker} narrow={false} />
          </div>
        )}
        <p className="mt-auto pt-2.5 text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.scenario.hintUrl")}</p>
      </div>
    </aside>
  );
}
