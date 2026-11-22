# stock-market-webapi image (Deploy Laptop Plan, D5). Build from the src folder:
#   docker build -f stock-market-webapi/Dockerfile -t tsx-webapi src
# Settings come from the environment (compose file): Aggregator__GrpcUrl, Api__ReadOnly.

# The SDK version matches src/global.json (10.0.103, patch roll-forward only).
FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
WORKDIR /src
COPY . .
RUN dotnet publish stock-market-webapi/stock-market.webapi.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The aspnet image listens on 8080 (ASPNETCORE_HTTP_PORTS). Health: GET /health.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "stock-market.webapi.dll"]
