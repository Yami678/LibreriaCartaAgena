FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Cartagena.Api.csproj ./
RUN dotnet restore Cartagena.Api.csproj
COPY . ./
RUN dotnet publish Cartagena.Api.csproj -c Release -o /app/out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Node.js 20: corre junto a la API el microservicio de login (Node/Express).
# La API C# hace de front y hace proxy de /api/v1/* a este proceso.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl xz-utils \
 && curl -fsSL https://nodejs.org/dist/v20.20.2/node-v20.20.2-linux-x64.tar.xz \
    | tar -xJ -C /usr/local --strip-components=1 \
 && apt-get purge -y curl xz-utils \
 && apt-get autoremove -y \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/out ./

# Microservicio de login: solo dependencias de produccion.
WORKDIR /app/login
COPY login/package.json login/package-lock.json ./
RUN npm ci --omit=dev
COPY login/src ./src

WORKDIR /app
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# La API C# escucha en 8080 (el puerto que usa Render) y tambien en 3000 como
# respaldo. El microservicio de login queda en 3001, interno, y la API C# le
# hace proxy en /api/v1/*.
ENV ASPNETCORE_URLS=http://+:8080;http://+:3000
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/entrypoint.sh"]
