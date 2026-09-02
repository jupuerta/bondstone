# Igual que el del frontend actual, y a propósito: lo único que da por supuesto del stack
# es que `npm run build` deja el sitio estático en dist/. Eso vale para Vite, Svelte, Solid,
# Vue o vanilla. Si acaba siendo Next o Astro en modo servidor, esta etapa cambia.
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite incrusta las variables en tiempo de build, no las lee en runtime.
ARG VITE_API_URL=/api
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
