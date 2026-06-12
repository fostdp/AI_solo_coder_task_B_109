FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci --registry=https://registry.npmmirror.com
COPY . .
RUN npm run build

FROM nginx:1.25-alpine
RUN apk add --no-cache brotli
COPY nginx/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
