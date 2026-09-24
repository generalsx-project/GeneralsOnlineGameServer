# Multi-stage build for GeneralsOnline Game Server (.NET 10)
# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS builder

WORKDIR /src

# Cache dependency restore
COPY GenServices.sln .
COPY GenOnlineService/GenOnlineService.csproj GenOnlineService/
RUN dotnet restore GenOnlineService/GenOnlineService.csproj

# Copy source code and build release
COPY GenOnlineService/ GenOnlineService/
WORKDIR /src/GenOnlineService
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Minimal Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-alpine AS runtime

# Install native dependencies needed by System.Drawing / crypto if needed
RUN apk add --no-cache tzdata icu-libs libstdc++

WORKDIR /app

# Copy published application binaries
COPY --from=builder /app/publish .

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production     DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false     LC_ALL=en_US.UTF-8     LANG=en_US.UTF-8

# Expose internal Kestrel HTTP port
EXPOSE 8001

ENTRYPOINT ["dotnet", "GenOnlineService.dll"]
