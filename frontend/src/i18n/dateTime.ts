// El reloj, no el «hace un rato». Dos intentos del mismo reto empezados con minutos de diferencia
// se leen igual en tiempo relativo («hace unos minutos» los dos), y entonces borrar uno parece no
// haber hecho nada. Una hora concreta no colisiona.
//
// El catálogo son `es` y `en` a secas; para fechas hacen falta las regiones: `en` sin más pone el
// mes delante y el reloj de 12 horas, y este producto escribe en inglés británico.
const REGION: Record<string, string> = { es: "es-ES", en: "en-GB" };

const PARTS: Intl.DateTimeFormatOptions = { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hour12: false };

export function shortDateTime(at: number, locale: string, now: number = Date.now()): string {
  const when = new Date(at);
  const options = when.getFullYear() === new Date(now).getFullYear() ? PARTS : { ...PARTS, year: "numeric" as const };

  return new Intl.DateTimeFormat(REGION[locale] ?? locale, options).format(when);
}
