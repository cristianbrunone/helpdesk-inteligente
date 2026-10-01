# syntax=docker/dockerfile:1
# Frontend (ADD §12): build com Node 24 e publicação estática num Nginx sem root, que também faz o
# proxy de /api para a API (mesma origem, sem CORS e sem URL de API no bundle).

FROM node:24.21.0-alpine3.23 AS build
WORKDIR /app
COPY package.json package-lock.json ./
# CA extra opcional (inspeção TLS corporativa): só para o npm ci; não vai para a imagem final.
RUN --mount=type=secret,id=ca_extra,target=/run/secrets/ca_extra \
    if [ -s /run/secrets/ca_extra ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/ca_extra; fi \
 && npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30.4-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
