# The panel (TASK 07a): built with Node, served by unprivileged nginx, which also forwards
# /api/panel (and nothing else under /api) to the .NET API inside office_net.
FROM node:24.21.0-alpine3.23 AS build
WORKDIR /web
ENV npm_config_fund=false npm_config_audit=false npm_config_update_notifier=false
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY . .
RUN npm run build \
 # a .gz beside each file, served as is by gzip_static: compressed once at the best level, not per request
 && find dist -type f \( -name '*.js' -o -name '*.css' -o -name '*.html' -o -name '*.svg' -o -name '*.json' \) -exec gzip -9 -k -n {} +

FROM nginxinc/nginx-unprivileged:1.30.4-alpine3.24
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.inc /etc/nginx/hazem-headers.inc
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
