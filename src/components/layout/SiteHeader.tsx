import { useEffect, useRef, useState } from "react";
import { Link, NavLink } from "react-router-dom";
import { useT } from "../../i18n";
import { Button } from "../ui/Button";
import { ArrowIcon, CloseIcon, LogoMark, MenuIcon } from "../ui/Icons";
import { LanguageToggle } from "./LanguageToggle";

export const FIRST_CHALLENGE = "/c/single-point-of-failure";

const ITEMS = [
  { key: "web.nav.challenges", to: "/challenges" },
  { key: "web.nav.rules", to: "/rules" },
  { key: "web.nav.model", to: "/model" },
  { key: "web.nav.mechanisms", to: "/mechanisms" },
  { key: "web.nav.about", to: "/about" },
];

export function SiteHeader() {
  const t = useT();
  const [open, setOpen] = useState(false);

  return (
    <header className="border-b border-line bg-bg">
      <div className="page flex h-[60px] items-center justify-between md:h-[68px]">
        <div className="flex items-center gap-10">
          <Link to="/" className="flex items-center gap-2.5 font-display text-[17px] font-bold tracking-tight text-ink no-underline">
            <LogoMark size={22} />
            ArchForge
          </Link>
          <nav aria-label={t("web.nav.menu")} className="hidden items-center gap-[26px] lg:flex">
            {ITEMS.map((item) => (
              <NavLink
                key={item.key}
                to={item.to}
                className={({ isActive }) =>
                  `text-small no-underline transition-colors ${
                    isActive
                      ? "font-semibold text-ink underline decoration-accent decoration-2 underline-offset-8"
                      : "text-ink-2 hover:text-ink"
                  }`
                }
              >
                {t(item.key)}
              </NavLink>
            ))}
          </nav>
        </div>

        <div className="flex items-center gap-2 md:gap-[18px]">
          <span className="hidden lg:inline-flex">
            <LanguageToggle />
          </span>
          <Button to={FIRST_CHALLENGE} size="sm" className="px-3 text-[13px] md:px-4 md:text-[14px]">
            {t("web.nav.start")}
          </Button>
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label={t("web.a11y.openMenu")}
            aria-expanded={open}
            aria-controls="site-menu"
            className="inline-flex size-11 items-center justify-center rounded-pill text-ink lg:hidden"
          >
            <MenuIcon size={24} />
          </button>
        </div>
      </div>

      {open && <MobileMenu onClose={() => setOpen(false)} />}
    </header>
  );
}

function MobileMenu({ onClose }: { onClose: () => void }) {
  const t = useT();
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = previous;
    };
  }, [onClose]);

  return (
    <div id="site-menu" role="dialog" aria-modal="true" aria-label={t("web.nav.menu")} className="fixed inset-0 z-50 flex flex-col bg-bg lg:hidden">
      <div className="flex h-[60px] items-center justify-between border-b border-line px-4">
        <span className="flex items-center gap-2 font-display text-[16px] font-bold">
          <LogoMark size={20} />
          ArchForge
        </span>
        <button ref={closeRef} type="button" onClick={onClose} aria-label={t("web.a11y.closeMenu")} className="inline-flex size-11 items-center justify-center rounded-pill">
          <CloseIcon size={22} />
        </button>
      </div>
      <nav aria-label={t("web.nav.menu")} className="hairline-rows flex flex-col px-4">
        {ITEMS.map((item) => (
          <NavLink
            key={item.key}
            to={item.to}
            onClick={onClose}
            className={({ isActive }) => `flex min-h-[52px] items-center justify-between text-[17px] no-underline ${isActive ? "font-semibold text-ink" : "text-ink"}`}
          >
            {t(item.key)}
            <ArrowIcon size={18} className="text-ink-3" />
          </NavLink>
        ))}
      </nav>
      <div className="mt-auto flex flex-col gap-4 border-t border-line px-4 py-4">
        <div className="flex items-center justify-between">
          <span className="eyebrow">{t("web.nav.language")}</span>
          <LanguageToggle />
        </div>
        <Button to={FIRST_CHALLENGE} size="md" className="w-full">
          {t("web.nav.start")}
        </Button>
      </div>
    </div>
  );
}
