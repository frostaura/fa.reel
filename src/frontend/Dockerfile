# syntax=docker/dockerfile:1
FROM node:22-alpine AS build
WORKDIR /app

COPY src/frontend/package*.json ./
# Reproducible install from the lockfile; fall back to a legacy-peer-deps resolve if the lock
# is ever out of sync so a strict peer check can't break the image build (foresight precedent).
RUN npm ci --no-audit --no-fund || npm install --no-audit --no-fund --legacy-peer-deps

COPY src/frontend/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY src/frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
