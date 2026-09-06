import { useEffect } from "react";
import { Route, Routes, useLocation } from "react-router-dom";
import { AboutPage } from "./pages/AboutPage";
import { ChallengePage } from "./pages/ChallengePage";
import { ChallengesPage } from "./pages/ChallengesPage";
import { EditorPage } from "./pages/EditorPage";
import { HomePage } from "./pages/HomePage";
import { LegalPage } from "./pages/LegalPage";
import { MechanismsPage } from "./pages/MechanismsPage";
import { ModelPage } from "./pages/ModelPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { RulesPage } from "./pages/RulesPage";
import { SharedPage } from "./pages/SharedPage";

export function App() {
  return (
    <>
      <ScrollToTop />
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/challenges" element={<ChallengesPage />} />
        <Route path="/c/:slug" element={<ChallengePage />} />
        <Route path="/a/:token" element={<SharedPage />} />
        <Route path="/e/:token" element={<EditorPage />} />
        <Route path="/rules" element={<RulesPage />} />
        <Route path="/rules/:token" element={<RulesPage />} />
        <Route path="/model" element={<ModelPage />} />
        <Route path="/mechanisms" element={<MechanismsPage />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/privacidad" element={<LegalPage kind="privacy" />} />
        <Route path="/terminos" element={<LegalPage kind="terms" />} />
        <Route path="/aviso-de-recoleccion" element={<LegalPage kind="collection" />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </>
  );
}

/** Cada página nueva empieza arriba, salvo que la dirección lleve un ancla. */
function ScrollToTop() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (hash === "") window.scrollTo(0, 0);
  }, [pathname, hash]);
  return null;
}
