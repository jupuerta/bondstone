#!/usr/bin/env node
// El único script en línea que queda en `index.html` es el arranque del tema, y entra en la CSP
// por su hash SHA-256 — no por `'unsafe-inline'`, que abriría justo lo que la política cierra.
//
// Un hash es exacto: un espacio de más en ese script y el navegador lo bloquea. El síntoma sería
// un destello de tema equivocado en cada carga, que nadie relaciona con haber tocado la CSP.
// Esto lo convierte en un fallo de construcción con el hash nuevo ya calculado.
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const html = readFileSync(join(here, "..", "index.html"), "utf8");
const conf = readFileSync(join(here, "..", "nginx.security-headers.conf"), "utf8");

// La directiva de verdad, no los comentarios que la explican — que también dicen `script-src`.
const policy = /^\s*add_header\s+Content-Security-Policy\s+"([^"]*)"/m.exec(conf)?.[1];
if (policy === undefined) {
  console.error("check:csp — nginx.security-headers.conf has no Content-Security-Policy header");
  process.exit(1);
}

const INLINE_SCRIPT = /<script(?![^>]*\ssrc=)[^>]*>([\s\S]*?)<\/script>/g;
const hashOf = (body) => `sha256-${createHash("sha256").update(body, "utf8").digest("base64")}`;

const needed = [...html.matchAll(INLINE_SCRIPT)].map((match) => hashOf(match[1]));
const declared = [...policy.matchAll(/'(sha256-[A-Za-z0-9+/=]+)'/g)].map((match) => match[1]);
const problems = [];

for (const hash of needed) {
  if (!declared.includes(hash))
    problems.push(`index.html carries an inline script whose hash is not in the policy — add '${hash}' to script-src`);
}

for (const hash of declared) {
  if (!needed.includes(hash))
    problems.push(`the policy allows '${hash}' and no inline script in index.html has that hash — remove it`);
}

// `'unsafe-inline'` en `script-src` desactiva los hashes por completo en los navegadores antiguos y
// convive con ellos en los nuevos: en ninguno de los dos casos es lo que queremos aquí.
const scriptSrc = /script-src([^;]*)/.exec(policy)?.[1] ?? "";
if (scriptSrc.includes("'unsafe-inline'"))
  problems.push("script-src carries 'unsafe-inline' — the point of the hash is that it does not");

if (problems.length > 0) {
  console.error(`check:csp — ${problems.length} problem(s):`);
  for (const problem of problems) console.error(`  · ${problem}`);
  process.exit(1);
}

console.log(`check:csp — ${needed.length} inline script(s), all covered by the policy.`);
