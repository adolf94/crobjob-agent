FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Api/Api.csproj", "src/Api/"]
COPY ["tests/Api.Tests/Api.Tests.csproj", "tests/Api.Tests/"]
RUN dotnet restore "src/Api/Api.csproj"

COPY ["src/Api/", "src/Api/"]
COPY ["tests/Api.Tests/", "tests/Api.Tests/"]
RUN dotnet publish "src/Api/Api.csproj" -c Release -o /app/publish

FROM node:24 AS web-build
WORKDIR /web

ARG VITE_AR_AUTH_AUTHORITY=https://auth.adolfrey.com/api
ARG VITE_AR_AUTH_CLIENT_ID=PLACEHOLDER_CLIENT_ID
ARG VITE_AR_AUTH_SCOPE=openid profile email
ENV VITE_AR_AUTH_AUTHORITY=$VITE_AR_AUTH_AUTHORITY \
    VITE_AR_AUTH_CLIENT_ID=$VITE_AR_AUTH_CLIENT_ID \
    VITE_AR_AUTH_SCOPE=$VITE_AR_AUTH_SCOPE

COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci
COPY src/Web/ .
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish ./
COPY --from=web-build /Api/wwwroot ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
