# Test-only image (docker compose profile "tests"): Meta/OpenAI stand-in for hazem-e2e.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.23 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY tests/Office.TestStub/ tests/Office.TestStub/
RUN dotnet publish tests/Office.TestStub/Office.TestStub.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.23-extra
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /out /app
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/Office.TestStub.dll"]
