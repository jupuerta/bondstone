# minimal-api-db

| | |
|---|---|
| Arquitectura | `8fK29x` · versión 1 |
| Exportado | 2026-08-16 |
| Motor de análisis | `scoringVersion: v4` |
| Formato | `exportVersion: 1` |

## Diagrama

```mermaid
flowchart LR
  Api["Api: Api"]
  Db["Database: Db"]

  Api -->|Sql| Db

  classDef down fill:#fee2e2,stroke:#dc2626,color:#7f1d1d
  class Api down
  class Db down
```

## Componentes

| Nombre | Tipo | Configuración |
|---|---|---|
| Api | Api | — |
| Db | Database | — |

## Conexiones

| Origen | Destino | Protocolo | Síncrona | Timeout | Reintentos | Circuit breaker | Fallback |
|---|---|---|---|---|---|---|---|
| Api | Db | Sql | sí | 5 s | — | no | no |

## Análisis

**Puntuación: 97 / 100** · 1 crítico · 1 aviso · 9 reglas superadas

| Área | Puntuación |
|---|---|
| Availability | 100 |
| Resilience | 100 |
| Coupling | 100 |
| Scalability | 100 |
| Security | 85 |
| Observability | 95 |

### 🔴 UNAUTHENTICATED_ENTRY — Security

**'Api' está expuesto sin autenticación.**

Es una puerta de entrada desde fuera y detrás hay una base de datos, así que cualquiera que conozca la dirección llega a los datos.

*Sugerencia:* Activa la autenticación en el punto de entrada, o pon delante un gateway que la haga.

Elementos: `Api`

### 🟡 NO_OBSERVABILITY — Observability

**No hay nada observando la arquitectura.**

Cuando algo falle no habrá dónde mirar: ni qué componente empezó, ni cuánto tardaba, ni cuántas peticiones se perdieron. El fallo se investiga con lo que se recuerde.

*Sugerencia:* Añade un componente de observabilidad y conéctale lo que quieras poder mirar cuando algo vaya mal.

### 🟢 SPOF — Availability

**Ningún componente concentra el funcionamiento del sistema.**

Se ha comprobado, componente a componente, qué dejaría de funcionar si cayera: ninguno se lleva por delante el resto del sistema.

### 🟢 MISSING_RESILIENCE — Resilience

**Las llamadas síncronas tienen protección frente a fallos.**

Cada llamada síncrona tiene un timeout que la acota y, cuando el destino es crítico, una alternativa por la que salir si no responde.

### 🟢 SYNC_CHAIN — Resilience

**No hay cadenas síncronas largas.**

Se han recorrido las llamadas síncronas y ninguna encadena suficientes componentes como para que las latencias se sumen.

### 🟢 TIMEOUT_TOO_SHORT — Resilience

**Los timeouts dan tiempo a que la llamada termine.**

Cada timeout es mayor que lo que cuesta esa llamada con todo funcionando, así que solo salta cuando algo va mal de verdad.

### 🟢 CYCLE — Coupling

**No hay dependencias circulares.**

Se han seguido las dependencias entre componentes y ninguna vuelve sobre sus pasos: todas se pueden desplegar y arrancar en algún orden.

### 🟢 HIGH_COUPLING — Coupling

**Ningún componente acumula demasiadas dependencias.**

Se ha contado de cuántos componentes depende cada uno y ninguno necesita tantas piezas como para ser difícil de desplegar o probar por separado.

### 🟢 ORPHAN_NODE — Coupling

**Todos los componentes están conectados a algo.**

Un componente sin llamadas entrantes ni salientes no participa en ningún flujo, así que ninguna otra regla puede decir nada sobre él.

### 🟢 MISSING_CACHE — Scalability

**Los accesos a base de datos están respaldados por caché o réplicas.**

Se ha mirado quién consulta cada base de datos: ninguna recibe suficiente carga directa como para convertirse en el límite de escalado del sistema.

### 🟢 EXPOSED_DATABASE — Security

**Las bases de datos están detrás de algo.**

Para llegar a los datos hay que pasar antes por una API o un gateway, que es donde se puede comprobar quién llama.

## Simulación

**Escenario:**

- `Db` caído

| Métrica | Valor |
|---|---|
| Componentes caídos | 2 de 2 |
| Componentes degradados | 0 de 2 |
| Radio de impacto | 100 % |
| Puntos de entrada afectados | 1 de 1 |
| Latencia nominal | 27 ms |
| Latencia con el escenario | 5005 ms |
| Peticiones afectadas | como mucho 100 % |
| Cuello de botella | `Db` |
| Amplificación de peticiones | ×1 |
| Amplificación de carga | ×1 |
| Encolado pendiente | 0 rps |

### Qué le pasa a cada componente

**🔴 `Db`** — Fallo inyectado en este componente.

**🔴 `Api`** — Su llamada a 'Db' no tiene alternativa cuando 'Db' no responde.

**🔴 `Api → Db`** — 'Db' no responde y la llamada no tiene alternativa.

---

## Petición para la IA

Eres un arquitecto de software senior. Arriba tienes una arquitectura, su diagrama y el
resultado de un analizador determinista, con la simulación de un escenario si se ejecutó uno.

Los hallazgos listados **ya son ciertos**: no hace falta que los redescubras ni que los pongas
en duda. Lo que te pido es lo que el analizador no puede hacer:

1. Ordénalos por lo que arreglarías primero, y explica por qué en ese orden.
2. Señala lo que las reglas **no** ven: elección de tecnología, límites entre servicios,
   nombres, o riesgos que no se deducen de la topología.
3. Si algo del diseño te parece correcto aunque el analizador lo penalice, dilo y razónalo.

Sé concreto y usa los nombres de los componentes.
