export function tableCell(text: string | null | undefined): string {
  if (!text) return "";

  let out = "";
  for (const c of text) {
    if (c === "|") out += "\\|";
    else if (c === "\r") continue;
    else if (c === "\n") out += " ";
    else out += c;
  }
  return out;
}

export function mermaidLabel(text: string | null | undefined): string {
  if (!text) return "";

  let out = "";
  for (const c of text) {
    if (c === '"') out += "#quot;";
    else if (c === "[" || c === "{") out += "(";
    else if (c === "]" || c === "}") out += ")";
    else if (c === "|") out += "/";
    else if (c === "\r") continue;
    else if (c === "\n") out += " ";
    else out += c;
  }
  return out;
}

export function mermaidIdentifier(name: string | null | undefined): string {
  let out = "";
  for (const c of name ?? "") {
    if ((c >= "A" && c <= "Z") || (c >= "a" && c <= "z") || (c >= "0" && c <= "9") || c === "_") {
      out += c;
    }
  }

  if (out.length === 0) return "n";

  return out[0] >= "0" && out[0] <= "9" ? `n${out}` : out;
}
