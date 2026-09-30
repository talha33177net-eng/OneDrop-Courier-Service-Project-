# The two images docker-compose.yml runs, from one build of the repository:
#   database  deploys the schema and data into a database: tools/db/publish.ps1 (dbup pre -> dacpac -> dbup),
#             the same script as on a developer's machine, given the connection string as its argument
#   web       the application
# Secrets never enter an image: .dockerignore leaves out every *.Local.json and .env.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Web/Web.csproj --configuration Release --output /app/web

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS database
RUN dotnet tool install --global microsoft.sqlpackage
ENV PATH="${PATH}:/root/.dotnet/tools"
WORKDIR /src
COPY . .
RUN dotnet build src/Database/Database.sqlproj && dotnet build "src/Database Update/Database Update.csproj"
ENTRYPOINT ["pwsh", "-NoProfile", "-File", "tools/db/publish.ps1"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=build /app/web .
# Sign-in cookies survive a restart when this folder is a volume (docker-compose.yml mounts one)
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID /home/app/.aspnet
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Web.dll"]
