FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend-dickifier.csproj packages.lock.json ./
RUN dotnet restore --locked-mode

COPY . .
RUN dotnet publish --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD ["dotnet", "backend-dickifier.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "backend-dickifier.dll"]
