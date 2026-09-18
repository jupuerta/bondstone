const CURSOR_CLASS = "af-col-resizing";

let active = 0;

export function beginColumnResize(): void {
  active += 1;
  document.body.classList.add(CURSOR_CLASS);
}

export function endColumnResize(): void {
  active = Math.max(0, active - 1);
  if (active === 0) document.body.classList.remove(CURSOR_CLASS);
}

export const resizingColumns = (): boolean => active > 0;
