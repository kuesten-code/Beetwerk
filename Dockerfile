# --- Frontend (React/Vite) bauen; landet in wwwroot des Backends ---
FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# --- Backend bauen ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY NuGet.Config ./
COPY src/Kuestencode.Beetwerk.Domain/Kuestencode.Beetwerk.Domain.csproj src/Kuestencode.Beetwerk.Domain/
COPY src/Kuestencode.Beetwerk.Data/Kuestencode.Beetwerk.Data.csproj src/Kuestencode.Beetwerk.Data/
COPY src/Kuestencode.Beetwerk.Api/Kuestencode.Beetwerk.Api.csproj src/Kuestencode.Beetwerk.Api/
RUN dotnet restore src/Kuestencode.Beetwerk.Api/Kuestencode.Beetwerk.Api.csproj
COPY src/ src/
COPY --from=frontend /src/src/Kuestencode.Beetwerk.Api/wwwroot src/Kuestencode.Beetwerk.Api/wwwroot
RUN dotnet publish src/Kuestencode.Beetwerk.Api/Kuestencode.Beetwerk.Api.csproj -c Release -o /app --no-restore

# --- Laufzeit ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ARG DOCKER_IMAGE_TAG=dev
ENV DOCKER_IMAGE_TAG=${DOCKER_IMAGE_TAG}
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DATA_DIR=/data \
    TZ=Europe/Berlin
COPY --from=backend /app ./
# Benannte Volumes übernehmen Besitzer und Rechte dieses Verzeichnisses.
RUN mkdir -p /data && chown app:app /data
VOLUME /data
EXPOSE 8080
USER app
HEALTHCHECK --interval=60s --timeout=10s --start-period=30s CMD ["dotnet", "Kuestencode.Beetwerk.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Kuestencode.Beetwerk.Api.dll"]
