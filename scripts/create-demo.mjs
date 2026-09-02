#!/usr/bin/env node
// Crea «la tienda» en dos versiones (rota y arreglada) en la API indicada y devuelve los enlaces
// de solo lectura que la portada necesita en VITE_DEMO_BEFORE_TOKEN y VITE_DEMO_AFTER_TOKEN.
//
//   node scripts/create-demo.mjs http://localhost:5012/api
//
// Comprueba, además, que el simulador dice lo que la portada promete: con Postgres caído, la
// rota pierde la entrada y la arreglada la conserva.
import { randomUUID } from "node:crypto";

const api = (process.argv[2] ?? "http://localhost:5012/api").replace(/\/$/, "");

async function call(method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${method} ${path} → HTTP ${response.status}`);
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

const node = (name, type, x, y, configuration = null) => ({ id: randomUUID(), type, name, position: { x, y }, configuration });
const call_ = (a, b, protocol, configuration = null, synchronous = true) => ({ id: randomUUID(), sourceNodeId: a.id, targetNodeId: b.id, protocol, synchronous, configuration });

function shop(fixed) {
  const cliente = node("Cliente", "Client", 0, 160);
  const gateway = node("Gateway de borde", "Gateway", 240, 160, { requestsPerSecond: 150, authentication: true, rateLimit: 300 });
  const apiNode = node("API de la tienda", "Api", 480, 160);
  const pedidos = node("Pedidos", "Service", 720, 60);
  const pagos = node("Pagos", "Service", 720, 260);
  const postgres = node("Postgres", "Database", 960, 60);
  const nodes = [cliente, gateway, apiNode, pedidos, pagos, postgres];
  const connections = [
    call_(cliente, gateway, "Http"),
    call_(gateway, apiNode, "Http", { timeoutSeconds: 5 }),
    call_(apiNode, pedidos, "Http", { timeoutSeconds: 5 }),
    call_(apiNode, pagos, "Http", { timeoutSeconds: 5 }),
  ];
  // Pedidos y Pagos comparten la base de datos: así Postgres es un punto único de fallo de verdad y el
  // hallazgo que enseña la portada habla del componente que se tumba.
  if (fixed) {
    const cache = node("Caché", "Cache", 960, -60);
    nodes.push(cache);
    connections.push(call_(pedidos, cache, "Http", { timeoutSeconds: 1 }));
    connections.push(call_(pedidos, postgres, "Sql", { timeoutSeconds: 5, fallback: true }));
    connections.push(call_(pagos, postgres, "Sql", { timeoutSeconds: 5, fallback: true }));
  } else {
    connections.push(call_(pedidos, postgres, "Sql", { timeoutSeconds: 5 }));
    connections.push(call_(pagos, postgres, "Sql", { timeoutSeconds: 5 }));
  }
  return { nodes, connections, postgres };
}

const result = {};
for (const [label, fixed] of [["before", false], ["after", true]]) {
  const created = await call("POST", "/architectures", { name: fixed ? "La tienda · arreglada" : "La tienda" });
  const { nodes, connections, postgres } = shop(fixed);
  await call("PUT", `/architectures/edit/${created.editToken}`, { name: created.name, version: created.version, nodes, connections });
  const simulation = await call("POST", `/architectures/${created.shareToken}/simulate`, { faults: [{ target: "Node", id: postgres.id, kind: "down" }], zones: [] });
  const entryDown = simulation.metrics.entryPointsAffected > 0;
  const expected = fixed ? !entryDown : entryDown;
  console.log(`${label}: share=${created.shareToken} edit=${created.editToken} · entrada ${entryDown ? "cae" : "aguanta"} ${expected ? "✓" : "✗ (no es lo que la portada promete)"}`);
  result[label] = created.shareToken;
}
console.log(`\nVITE_DEMO_BEFORE_TOKEN=${result.before}\nVITE_DEMO_AFTER_TOKEN=${result.after}\nVITE_DEMO_FAULT_NODE=Postgres`);
