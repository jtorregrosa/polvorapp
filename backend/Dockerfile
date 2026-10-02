# syntax=docker/dockerfile:1
# API image (ADR-0006). Runtime: chiseled (non-root, no shell) with ICU, required for ca-ES-valencia.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
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
RUN dotnet restore src/PolvorApp.Api/PolvorApp.Api.csproj

COPY src/ src/
ARG APP_VERSION=0.1.0
ARG GIT_COMMIT=unknown
# The committed contract is checked in CI; the image build does not regenerate it.
RUN dotnet publish src/PolvorApp.Api/PolvorApp.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:Version=${APP_VERSION} -p:SourceRevisionId=${GIT_COMMIT} \
      -p:OpenApiGenerateDocuments=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:6385dc0eaef704fad88d3f65c334e791a371bbe448f52ca39d83d2df49251e28 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PolvorApp.Api.dll"]
