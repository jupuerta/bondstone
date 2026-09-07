# resilient-async

| Arquitectura | `8fK29x` · versión 1 |
|---|---|
| Exportado | 2026-08-16 |
| Motor de análisis | `scoringVersion: v12` |
| Formato | `exportVersion: 3` |

## Diagrama

```mermaid
flowchart LR
  Api1["Api: Api1"]
  Api2["Api: Api2"]
  Db["Database: Db"]
  Orders["Service: Orders"]
  Queue["Queue: Queue"]
  Cache["Cache: Cache"]
  LB["LoadBalancer: LB"]

  Api1 -->|Http| Cache
  Api1 -.->|Amqp| Queue
  Api2 -->|Http| Cache
  Api2 -.->|Amqp| Queue
  Cache -->|Sql| Db
  LB -->|Http| Api1
  LB -->|Http| Api2
  Orders -->|Sql| Db
  Queue -.->|Amqp| Orders
```

## Componentes

| Nombre | Tipo | Configuración |
|---|---|---|
| Api1 | Api | — |
| Api2 | Api | — |
| Db | Database | — |
| Orders | Service | — |
| Queue | Queue | — |
| Cache | Cache | — |
| LB | LoadBalancer | — |

## Conexiones

| Origen | Destino | Protocolo | Síncrona | Timeout | Reintentos | Circuit breaker | Fallback |
|---|---|---|---|---|---|---|---|
| Api1 | Cache | Http | sí | 1 s | — | no | sí |
| Api1 | Queue | Amqp | no | — | — | no | no |
| Api2 | Cache | Http | sí | 1 s | — | no | sí |
| Api2 | Queue | Amqp | no | — | — | no | no |
| Cache | Db | Sql | sí | 5 s | — | sí | sí |
| LB | Api1 | Http | sí | 3 s | — | sí | no |
| LB | Api2 | Http | sí | 3 s | — | sí | no |
| Orders | Db | Sql | sí | 5 s | — | sí | sí |
| Queue | Orders | Amqp | no | — | — | no | no |

## Análisis

**Puntuación: 96 / 100** · 1 crítico · 2 avisos · 14 reglas superadas

| Área | Puntuación |
|---|---|
| Availability | 95 |
| Resilience | 95 |
| Coupling | 100 |
| Scalability | 100 |
| Security | 100 |
| Observability | 85 |
| Cost | sin reglas aplicables |

### 🔴 NO_OBSERVABILITY — Observability

**El sistema está ciego: ninguno de sus 7 componentes está observado.**

No hay ni una sola conexión a la observabilidad en todo el diagrama, así que cuando algo falle no se sabrá qué falló, desde cuándo ni cuál fue la causa. Este hallazgo cubre el sistema entero y se cobra una sola vez: en cuanto conectes el primer componente, este crítico se convierte en un aviso por cada componente que siga sin conectar, y la puntuación del área mejora con cada conexión.

*Sugerencia:* Añade un componente de observabilidad y conéctale los componentes uno a uno, empezando por la puerta de entrada y el sistema de registro: son los dos que primero querrás mirar cuando algo vaya mal.

Elementos: `LB`, `Api1`, `Api2`, `Cache`, `Queue`, `Orders`, `Db`

### 🟡 SPOF — Availability

**'Db' es un punto único de fallo: 2 componentes dependen de él sin redundancia.**

Ninguna petición deja de servirse si 'Db' cae: todo lo que lo llama lo hace en asíncrono o con una respuesta alternativa. Lo que se pierde no son peticiones, sino lo que hay detrás — el estado que guarda, o el trabajo que no llega a hacerse — y eso no se mide aquí.

*Sugerencia:* Añade réplicas con failover automático y verifica que promocionar una nueva primaria no requiere intervención manual. Una réplica de lectura reparte carga; no releva a la primaria cuando cae.

Elementos: `Db`

### 🟡 QUEUE_NO_DLQ — Resilience

**'Queue' no tiene dead letter queue.**

Un mensaje que no se puede procesar se reintenta, vuelve a fallar y desaparece sin dejar rastro. Nadie sabe qué se ha perdido.

*Sugerencia:* Activa la dead letter queue para que lo que falle quede en algún sitio donde se pueda revisar.

Elementos: `Queue`

### 🟢 ASYNC_NO_QUEUE — Resilience

**Toda llamada que no espera tiene una cola donde su trabajo puede esperar.**

Cada llamada asíncrona entre componentes tuyos pasa por una cola o un topic: si quien procesa no está, el mensaje queda escrito y se recupera después.

### 🟢 CACHE_AS_STORE — Resilience

**Cada caché tiene un origen detrás.**

Todo lo que guarda una caché existe también en un sistema de registro, así que perderla cuesta latencia y no datos.

### 🟢 IMPLAUSIBLE_DEPENDENCY — Resilience

**Cada flecha une dos cosas que pueden hablarse.**

Se ha comprobado, flecha a flecha, que los dos extremos describen una llamada que puede ocurrir realmente: ningún almacén llama a nadie, ningún componente tuyo llama a los usuarios, el CDN habla con un origen y el planificador dispara trabajo.

### 🟢 MISSING_RESILIENCE — Resilience

**Las llamadas síncronas tienen protección frente a fallos.**

Cada llamada síncrona tiene un timeout que la acota y, cuando el destino es crítico, una alternativa por la que salir si no responde.

### 🟢 SYNC_CHAIN — Resilience

**No hay cadenas síncronas largas.**

Se han recorrido las llamadas síncronas y ninguna encadena suficientes servicios como para que las latencias se sumen.

### 🟢 TIMEOUT_TOO_LONG — Resilience

**Los timeouts son límites, no esperas.**

Cada timeout queda por debajo de lo que el modelo considera una espera sin límite, así que cuando el destino se cuelgue, la llamada fallará a tiempo de que se pueda hacer algo al respecto.

### 🟢 TIMEOUT_TOO_SHORT — Resilience

**Los timeouts dan tiempo a que la llamada termine.**

Cada timeout es mayor que lo que cuesta esa llamada con todo funcionando, así que solo salta cuando algo va mal de verdad.

### 🟢 CYCLE — Coupling

**No hay dependencias circulares.**

Se han seguido las dependencias entre componentes y ninguna vuelve sobre sus pasos: todas se pueden desplegar y arrancar en algún orden.

### 🟢 HIGH_COUPLING — Coupling

**Ningún componente acumula demasiadas dependencias.**

Se ha contado de cuántos componentes depende cada uno, y ninguno necesita tantos como para resultar difícil de desplegar o probar por separado.

### 🟢 ORPHAN_NODE — Coupling

**Todos los componentes están conectados a algo.**

Un componente sin llamadas entrantes ni salientes no participa en ningún flujo, así que ninguna otra regla puede decir nada sobre él.

### 🟢 QUEUE_NO_CONSUMER — Coupling

**Todo lo publicado tiene quien lo reciba.**

Cada cola y cada topic a los que alguien publica entregan sus mensajes a algún componente, así que el trabajo que se les confía llega a hacerse.

### 🟢 SHARED_DATABASE — Coupling

**Ningún almacén es compartido por varios servicios.**

Cada base de datos y cada almacenamiento de objetos tiene como mucho un servicio que lo usa, así que cada servicio puede cambiar sus datos sin afectar a los demás.

### 🟢 MISSING_CACHE — Scalability

**Los accesos a base de datos están respaldados por caché o réplicas.**

Se ha comprobado quién consulta cada base de datos: ninguna recibe suficiente carga directa como para convertirse en el límite de escalado del sistema.

### 🟢 EXPOSED_DATABASE — Security

**Los datos están detrás de algo.**

Para llegar a los datos hay que pasar antes por una API o un gateway, que es donde se puede comprobar quién llama.

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
