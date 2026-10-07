# syntax=docker/dockerfile:1
# API image (ADR-0006). Runtime: chiseled (non-root, no shell) with ICU, required for ca-ES-valencia.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src

# Project files first for restore caching. Add each new module project here (and its .Contracts).
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props PolvorApp.slnx ./
COPY src/PolvorApp.Api/PolvorApp.Api.csproj src/PolvorApp.Api/
COPY src/PolvorApp.SharedKernel/PolvorApp.SharedKernel.csproj src/PolvorApp.SharedKernel/
COPY src/Modules/AuditPrivacy/PolvorApp.AuditPrivacy/PolvorApp.AuditPrivacy.csproj src/Modules/AuditPrivacy/PolvorApp.AuditPrivacy/
COPY src/Modules/AuditPrivacy/PolvorApp.AuditPrivacy.Contracts/PolvorApp.AuditPrivacy.Contracts.csproj src/Modules/AuditPrivacy/PolvorApp.AuditPrivacy.Contracts/
COPY src/Modules/IdentityAccess/PolvorApp.IdentityAccess/PolvorApp.IdentityAccess.csproj src/Modules/IdentityAccess/PolvorApp.IdentityAccess/
COPY src/Modules/IdentityAccess/PolvorApp.IdentityAccess.Contracts/PolvorApp.IdentityAccess.Contracts.csproj src/Modules/IdentityAccess/PolvorApp.IdentityAccess.Contracts/
COPY src/Modules/FederationCatalog/PolvorApp.FederationCatalog/PolvorApp.FederationCatalog.csproj src/Modules/FederationCatalog/PolvorApp.FederationCatalog/
COPY src/Modules/FederationCatalog/PolvorApp.FederationCatalog.Contracts/PolvorApp.FederationCatalog.Contracts.csproj src/Modules/FederationCatalog/PolvorApp.FederationCatalog.Contracts/
COPY src/Modules/ArquebusierRegistry/PolvorApp.ArquebusierRegistry/PolvorApp.ArquebusierRegistry.csproj src/Modules/ArquebusierRegistry/PolvorApp.ArquebusierRegistry/
COPY src/Modules/ArquebusierRegistry/PolvorApp.ArquebusierRegistry.Contracts/PolvorApp.ArquebusierRegistry.Contracts.csproj src/Modules/ArquebusierRegistry/PolvorApp.ArquebusierRegistry.Contracts/
COPY src/Modules/ComplianceInsights/PolvorApp.ComplianceInsights/PolvorApp.ComplianceInsights.csproj src/Modules/ComplianceInsights/PolvorApp.ComplianceInsights/
COPY src/Modules/ComplianceInsights/PolvorApp.ComplianceInsights.Contracts/PolvorApp.ComplianceInsights.Contracts.csproj src/Modules/ComplianceInsights/PolvorApp.ComplianceInsights.Contracts/
COPY src/Modules/FestivalEditions/PolvorApp.FestivalEditions/PolvorApp.FestivalEditions.csproj src/Modules/FestivalEditions/PolvorApp.FestivalEditions/
COPY src/Modules/FestivalEditions/PolvorApp.FestivalEditions.Contracts/PolvorApp.FestivalEditions.Contracts.csproj src/Modules/FestivalEditions/PolvorApp.FestivalEditions.Contracts/
COPY src/Modules/ComparsaOrders/PolvorApp.ComparsaOrders/PolvorApp.ComparsaOrders.csproj src/Modules/ComparsaOrders/PolvorApp.ComparsaOrders/
COPY src/Modules/ComparsaOrders/PolvorApp.ComparsaOrders.Contracts/PolvorApp.ComparsaOrders.Contracts.csproj src/Modules/ComparsaOrders/PolvorApp.ComparsaOrders.Contracts/
COPY src/Modules/Billing/PolvorApp.Billing/PolvorApp.Billing.csproj src/Modules/Billing/PolvorApp.Billing/
COPY src/Modules/Billing/PolvorApp.Billing.Contracts/PolvorApp.Billing.Contracts.csproj src/Modules/Billing/PolvorApp.Billing.Contracts/
COPY src/Modules/Exports/PolvorApp.Exports/PolvorApp.Exports.csproj src/Modules/Exports/PolvorApp.Exports/
COPY src/Modules/Exports/PolvorApp.Exports.Contracts/PolvorApp.Exports.Contracts.csproj src/Modules/Exports/PolvorApp.Exports.Contracts/
COPY src/Modules/Distribution/PolvorApp.Distribution/PolvorApp.Distribution.csproj src/Modules/Distribution/PolvorApp.Distribution/
COPY src/Modules/Distribution/PolvorApp.Distribution.Contracts/PolvorApp.Distribution.Contracts.csproj src/Modules/Distribution/PolvorApp.Distribution.Contracts/
COPY src/Modules/Notifications/PolvorApp.Notifications/PolvorApp.Notifications.csproj src/Modules/Notifications/PolvorApp.Notifications/
COPY src/Modules/Notifications/PolvorApp.Notifications.Contracts/PolvorApp.Notifications.Contracts.csproj src/Modules/Notifications/PolvorApp.Notifications.Contracts/
COPY src/Modules/Badges/PolvorApp.Badges/PolvorApp.Badges.csproj src/Modules/Badges/PolvorApp.Badges/
COPY src/Modules/Badges/PolvorApp.Badges.Contracts/PolvorApp.Badges.Contracts.csproj src/Modules/Badges/PolvorApp.Badges.Contracts/
# An optional `proxy_ca` build secret (a full CA bundle) is trusted for the download, for networks
# that inspect TLS such as Claude Code cloud sessions (compose.cloud.yaml); it never enters a layer.
RUN --mount=type=secret,id=proxy_ca \
    if [ -f /run/secrets/proxy_ca ]; then export SSL_CERT_FILE=/run/secrets/proxy_ca; fi; \
    dotnet restore src/PolvorApp.Api/PolvorApp.Api.csproj

COPY src/ src/
# The seed's generated faces and emblems, published as SyntheticData/ (realistic-seed-data).
COPY synthetic-data/ synthetic-data/
ARG APP_VERSION=0.1.0
ARG GIT_COMMIT=unknown
# The committed contract is checked in CI; the image build does not regenerate it.
RUN dotnet publish src/PolvorApp.Api/PolvorApp.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:Version=${APP_VERSION} -p:SourceRevisionId=${GIT_COMMIT} \
      -p:OpenApiGenerateDocuments=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:00e0ad6a7ef8c0c1391b87f05c7ac757a15740455688f2bfcd146a3f4b987efd AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PolvorApp.Api.dll"]
