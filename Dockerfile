# Igual que el del frontend actual, y a propósito: lo único que da por supuesto del stack
# es que `npm run build` deja el sitio estático en dist/. Eso vale para Vite, Svelte, Solid,
# Vue o vanilla. Si acaba siendo Next o Astro en modo servidor, esta etapa cambia.
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite incrusta las variables en tiempo de build, no las lee en runtime. El `.env` del repo no
# entra en el contexto (.dockerignore), así que todo lo que la web necesita llega por aquí; sin
# los tokens de la demostración la portada simplemente no la enseña.
ARG VITE_API_URL=/api
ENV VITE_API_URL=$VITE_API_URL
ARG VITE_DEMO_BEFORE_TOKEN=
ENV VITE_DEMO_BEFORE_TOKEN=$VITE_DEMO_BEFORE_TOKEN
ARG VITE_DEMO_AFTER_TOKEN=
ENV VITE_DEMO_AFTER_TOKEN=$VITE_DEMO_AFTER_TOKEN
ARG VITE_DEMO_FAULT_NODE=Postgres
ENV VITE_DEMO_FAULT_NODE=$VITE_DEMO_FAULT_NODE
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
# Las cabeceras de seguridad viven aparte porque `add_header` no se hereda: cada `location` que
# declare uno propio tiene que volver a incluirlas. Si este COPY falta, nginx no arranca y lo dice.
COPY nginx.security-headers.conf /etc/nginx/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
