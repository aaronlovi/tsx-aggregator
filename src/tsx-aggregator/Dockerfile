# tsx-aggregator image (Deploy Laptop Plan, D3). Build from the src folder:
#   docker build -f tsx-aggregator/Dockerfile -t tsx-aggregator src
# Settings come from the environment (compose file); see deploy/README.md.

# The SDK version matches src/global.json (10.0.103, patch roll-forward only).
FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
WORKDIR /src
COPY . .
RUN dotnet publish tsx-aggregator/tsx-aggregator.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Google Chrome stable for the RawCollector (Ubuntu's chromium package is a snap stub that does not run in a container).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget fonts-liberation \
    && wget -q -O /tmp/chrome.deb https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get install -y --no-install-recommends /tmp/chrome.deb \
    && rm /tmp/chrome.deb \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Container defaults. Chrome runs without its sandbox (a container has no user namespaces for it) and with /tmp
# instead of the small /dev/shm. gRPC must listen on all interfaces inside the container; the compose file
# publishes it on 127.0.0.1 only. Logs go to stdout only (docker logs).
ENV Scraper__BrowserExecutablePath=/usr/bin/google-chrome-stable \
    Scraper__BrowserArgs__0=--no-sandbox \
    Scraper__BrowserArgs__1=--disable-dev-shm-usage \
    Ports__GrpcListenOnAllInterfaces=true \
    Logs__WriteFile=false \
    DOTNET_ENVIRONMENT=Production

EXPOSE 7001
USER $APP_UID
ENTRYPOINT ["dotnet", "tsx-aggregator.dll"]
