import { useT } from "../../../i18n";
import { Skeleton } from "../../ui/Skeleton";

export function EditorLoading() {
  const t = useT();
  return (
    <div className="flex flex-1 flex-col" aria-busy>
      <div className="flex h-14 items-center gap-4 border-b border-panel-line bg-panel-2 px-4">
        <Skeleton width={120} height={16} dark />
        <Skeleton width={200} height={14} dark />
      </div>
      <div className="flex flex-1 flex-col items-center justify-center gap-3 bg-canvas p-6">
        <Skeleton width="60%" height={62} dark />
        <span className="font-mono text-label text-panel-ink-3">{t("ui.loading")}</span>
      </div>
    </div>
  );
}
