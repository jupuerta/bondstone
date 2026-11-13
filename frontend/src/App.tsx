import { Suspense, lazy, useEffect, type ReactNode } from "react";
import { Route, Routes, useLocation } from "react-router-dom";
import { ChallengesPage } from "./pages/ChallengesPage";
import { HomePage } from "./pages/HomePage";
import { LegalPage } from "./pages/LegalPage";
import { MechanismsPage } from "./pages/MechanismsPage";
import { ModelPage } from "./pages/ModelPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RulesPage } from "./pages/RulesPage";
import { useT } from "./i18n";

/*
 * Las tres que traen el lienzo llegan aparte. Importadas como las demás, `@xyflow/react` y todo el
 * editor entraban en el primer fichero que descarga la portada — 193 KB de motor de lienzo antes
 * de leer un titular que es texto. Quien abre el enlace desde el móvil paga eso y no lo usa.
 *
 * `ChallengePage` está aquí por lo mismo: es la página de un reto, no la portada, y la portada ya
 * enlaza a ella. Lo que queda estático es lo que se lee de una sentada y pesa lo que pesa el texto.
 */
const ChallengePage = lazy(() => import("./pages/ChallengePage").then((m) => ({ default: m.ChallengePage })));
const EditorPage = lazy(() => import("./pages/EditorPage").then((m) => ({ default: m.EditorPage })));
const SharedPage = lazy(() => import("./pages/SharedPage").then((m) => ({ default: m.SharedPage })));

export function App() {
  return (
    <>
      <ScrollToTop />
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/challenges" element={<ChallengesPage />} />
        <Route path="/c/:slug" element={<Deferred><ChallengePage /></Deferred>} />
        <Route path="/a/:token" element={<Deferred tone="canvas"><SharedPage /></Deferred>} />
        <Route path="/e/:token" element={<Deferred tone="canvas"><EditorPage /></Deferred>} />
        <Route path="/rules" element={<RulesPage />} />
        <Route path="/rules/:token" element={<RulesPage />} />
        <Route path="/model" element={<ModelPage />} />
        <Route path="/mechanisms" element={<MechanismsPage />} />
        <Route path="/privacidad" element={<LegalPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </>
  );
}

/**
 * El hueco mientras llega el trozo. Del color que va a tener la página cuando llegue —claro las de
 * lectura, lienzo las dos del editor—, porque un fondo blanco de dos décimas delante del editor
 * oscuro se ve como un fallo y no como una espera.
 */
function Deferred({ tone = "page", children }: { tone?: "page" | "canvas"; children: ReactNode }) {
  return <Suspense fallback={<RouteLoading tone={tone} />}>{children}</Suspense>;
}

function RouteLoading({ tone }: { tone: "page" | "canvas" }) {
  const t = useT();

  return (
    <div className={`min-h-screen ${tone === "canvas" ? "bg-canvas" : "bg-bg"}`} aria-busy>
      <span className="sr-only">{t("ui.loading")}</span>
    </div>
  );
}

/*
 * Cada página nueva empieza arriba, salvo que la dirección lleve un ancla.
 *
 * El navegador resuelve el ancla una sola vez, al terminar de leer el documento, y en ese momento
 * aquí no hay más que un div vacío: lo que el ancla busca lo pinta React después. Por eso hay que
 * volver a buscarlo, y por eso se reintenta: una ruta que llega en su propio trozo —o una lista que
 * espera al servidor— todavía no ha montado el destino en el primer fotograma.
 *
 * Y por eso se vuelve a apuntar cuando acaban de llegar las tipografías: en una carga en frío el
 * texto de arriba cambia de alto después del salto y el destino se va de la pantalla. Se deja de
 * insistir en cuanto el visitante mueve la página, que ya ha dicho a dónde quiere mirar.
 */
const ANCHOR_FRAMES = 60;

function ScrollToTop() {
  const { pathname, hash } = useLocation();

  useEffect(() => {
    if (hash === "") {
      window.scrollTo(0, 0);
      return;
    }

    const id = decodeURIComponent(hash.slice(1));
    let frame = 0;
    let left = ANCHOR_FRAMES;
    let given = false;

    const give = () => {
      given = true;
      cancelAnimationFrame(frame);
    };

    const land = (target: HTMLElement) => {
      if (!given) target.scrollIntoView({ block: "start" });
    };

    const seek = () => {
      const target = document.getElementById(id);
      if (target) {
        land(target);
        void document.fonts?.ready.then(() => land(target));
        return;
      }
      if (--left > 0) frame = requestAnimationFrame(seek);
    };

    seek();
    window.addEventListener("wheel", give, { passive: true });
    window.addEventListener("touchstart", give, { passive: true });
    window.addEventListener("keydown", give);

    return () => {
      give();
      window.removeEventListener("wheel", give);
      window.removeEventListener("touchstart", give);
      window.removeEventListener("keydown", give);
    };
  }, [pathname, hash]);

  return null;
}
