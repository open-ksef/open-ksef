FROM node:20-alpine AS build
WORKDIR /app

COPY OpenKSeF.Portal.Web/package*.json ./
RUN npm ci

COPY OpenKSeF.Portal.Web/ ./
RUN npm run build

FROM nginx:alpine
COPY OpenKSeF.Portal.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/build /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
