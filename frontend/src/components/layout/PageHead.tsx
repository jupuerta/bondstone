import type { ReactNode } from "react";

interface PageHeadProps {
  eyebrow: string;
  title: string;
  lead: string;
  aside?: ReactNode;
}

/** Cabecera de página interior: etiqueta, título de 64 px, entradilla, y algo a la derecha si hace falta. */
export function PageHead({ eyebrow, title, lead, aside }: PageHeadProps) {
  return (
    <section className="border-b border-line">
      <div className="page flex flex-col gap-4 py-8 md:flex-row md:items-end md:justify-between md:gap-12 md:py-16 md:pt-20">
        <div className="flex max-w-[880px] flex-col gap-4 md:gap-[18px]">
          <span className="eyebrow eyebrow-accent">{eyebrow}</span>
          <h1 className="text-[40px] leading-none tracking-[-0.03em] md:text-page">{title}</h1>
          <p className="text-[17px] leading-normal text-ink-2 md:text-lead">{lead}</p>
        </div>
        {aside}
      </div>
    </section>
  );
}
