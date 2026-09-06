# OpenSignature Web (React / Vite → nginx)
# Build from repository root:
#   docker build -f docker/web/Dockerfile -t opensignature-web .

FROM node:22-alpine AS build
WORKDIR /web

COPY src/OpenSignature.Web/package.json src/OpenSignature.Web/package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi

COPY src/OpenSignature.Web/ ./

# Empty base URL → same-origin /api (proxied by nginx to the Api service).
ARG VITE_API_BASE_URL=
ARG VITE_TENANT_ID=tenant-demo
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_TENANT_ID=$VITE_TENANT_ID

RUN npm run build

FROM nginx:1.27-alpine AS final
COPY docker/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
  CMD wget -qO- http://127.0.0.1/ >/dev/null || exit 1
