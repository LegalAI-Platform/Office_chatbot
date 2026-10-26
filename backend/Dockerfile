# Builds the API and the CLI into one image. Runtime runs as the non-root "app" user.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-alpine3.24 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Restore first so package downloads are cached between code changes.
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props Office.sln ./
COPY src/Office.Domain/Office.Domain.csproj src/Office.Domain/
COPY src/Office.Application/Office.Application.csproj src/Office.Application/
COPY src/Office.Infrastructure/Office.Infrastructure.csproj src/Office.Infrastructure/
COPY src/Office.Api/Office.Api.csproj src/Office.Api/
COPY src/Office.Cli/Office.Cli.csproj src/Office.Cli/
RUN dotnet restore src/Office.Api/Office.Api.csproj && dotnet restore src/Office.Cli/Office.Cli.csproj

COPY src/ src/
RUN dotnet publish src/Office.Api/Office.Api.csproj -c Release --no-restore -o /out/api \
 && dotnet publish src/Office.Cli/Office.Cli.csproj -c Release --no-restore -o /out/cli

# "-extra" variant includes ICU and tzdata (Africa/Cairo).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24-extra AS runtime
WORKDIR /app
ENV TZ=Africa/Cairo ASPNETCORE_HTTP_PORTS=8080 DOTNET_CLI_TELEMETRY_OPTOUT=1
# Npgsql loads GSSAPI at connect time; without it every connection logs an error.
RUN apk add --no-cache krb5-libs
COPY --from=build /out/api /app/api
COPY --from=build /out/cli /app/cli
# The panel's Data Protection keys (Panel:KeysPath): the volume mounted here takes this owner.
RUN mkdir -p /app/keys && chown $APP_UID /app/keys
USER $APP_UID
EXPOSE 8080
WORKDIR /app/api
ENTRYPOINT ["dotnet", "/app/api/Office.Api.dll"]
