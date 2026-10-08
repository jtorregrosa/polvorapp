# syntax=docker/dockerfile:1
# Web image (ADR-0006): builds the SPA and serves it with an unprivileged nginx.
# Build context: repository root (the client is generated from contracts/openapi.json).
FROM node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS build
WORKDIR /repo/frontend
COPY frontend/package.json frontend/package-lock.json ./
# Optional `proxy_ca` build secret: see backend/Dockerfile.
RUN --mount=type=secret,id=proxy_ca \
    if [ -f /run/secrets/proxy_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/proxy_ca; fi; \
    npm ci --ignore-scripts
COPY contracts/ /repo/contracts/
COPY frontend/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.31-alpine@sha256:b9241c6e7b8e9a862f129d8d4199ab64b10390949a78bdd5603379b32c844083 AS runtime
COPY frontend/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY frontend/nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /repo/frontend/dist /usr/share/nginx/html
EXPOSE 8080
