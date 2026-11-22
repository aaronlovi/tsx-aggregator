# deep-value site image (Deploy Laptop Plan, D5): the built Angular app served by nginx, which also forwards
# /api/... to the web API with the /api prefix removed. The API keeps its own paths (/companies, ...), which
# overlap the app's routes, hence the prefix. Build from the deep-value folder:
#   docker build -t tsx-web deep-value
# Settings: API_UPSTREAM (default http://tsx-webapi:8080).

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY docker/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/deep-value/browser /usr/share/nginx/html
# The app reads its API address at start-up; in the container it is the same origin under /api.
COPY docker/app.config.json /usr/share/nginx/html/assets/app.config.json
# 15-local-resolvers.envsh puts the container's DNS servers in NGINX_LOCAL_RESOLVERS (Docker or Kubernetes alike).
ENV API_UPSTREAM=http://tsx-webapi:8080 \
    NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1
EXPOSE 8080
