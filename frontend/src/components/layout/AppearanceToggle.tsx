import { editorTheme, webTheme } from "../../domain/appearance";
import type { EditorTheme } from "../../domain/editorPrefs";
import { useAppearance } from "../../hooks/useAppearance";
import { useT } from "../../i18n";
import { MoonIcon, SunIcon } from "../ui/Icons";
import { Segmented } from "../ui/Segmented";

type Surface = "web" | "editor";

interface AppearanceProps {
  surface?: Surface;
  dark?: boolean;
}

/** Aspecto en la barra: un botón que lleva al otro modo. */
export function AppearanceToggle({ surface = "web", dark = false }: AppearanceProps) {
  const t = useT();
  const [appearance, choose] = useAppearance();
  const theme = themeOf(surface, appearance);
  const next: EditorTheme = theme === "light" ? "dark" : "light";
  const label = t(next === "light" ? "editor.theme.toLight" : "editor.theme.toDark");

  return (
    <button
      type="button"
      onClick={() => choose(next)}
      aria-label={label}
      title={label}
      className={`inline-flex size-8 shrink-0 items-center justify-center rounded-pill border transition-colors ${
        dark ? "border-panel-line text-panel-ink-2 hover:border-panel-line-2 hover:text-panel-ink" : "border-line-2 text-ink-2 hover:border-ink-3 hover:text-ink"
      }`}
    >
      {theme === "light" ? <MoonIcon size={16} /> : <SunIcon size={16} />}
    </button>
  );
}

/** Aspecto en un menú estrecho: el mismo cambio, con las palabras. */
export function AppearanceSegmented({ surface = "web", dark = false }: AppearanceProps) {
  const t = useT();
  const [appearance, choose] = useAppearance();

  return (
    <Segmented
      dark={dark}
      label={t("editor.theme.label")}
      value={themeOf(surface, appearance)}
      onChange={choose}
      options={[
        { value: "light", label: t("editor.theme.light") },
        { value: "dark", label: t("editor.theme.dark") },
      ]}
    />
  );
}

const themeOf = (surface: Surface, appearance: Parameters<typeof editorTheme>[0]): EditorTheme =>
  surface === "editor" ? editorTheme(appearance) : webTheme(appearance);
