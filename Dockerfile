FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN mkdir -p /src/src/ReceitasMercado.Api && npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY global.json ReceitasMercado.sln ./
COPY src/ReceitasMercado.Domain/*.csproj src/ReceitasMercado.Domain/
COPY src/ReceitasMercado.Api/*.csproj src/ReceitasMercado.Api/
COPY tests/ReceitasMercado.UnitTests/*.csproj tests/ReceitasMercado.UnitTests/
COPY tests/ReceitasMercado.IntegrationTests/*.csproj tests/ReceitasMercado.IntegrationTests/
RUN dotnet restore src/ReceitasMercado.Api/ReceitasMercado.Api.csproj
COPY src/ src/
COPY --from=web /src/src/ReceitasMercado.Api/wwwroot/ src/ReceitasMercado.Api/wwwroot/
RUN dotnet publish src/ReceitasMercado.Api/ReceitasMercado.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=api --chown=app:app /app ./
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ForwardedHeaders__Enabled=true
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q ok <&3"]
ENTRYPOINT ["dotnet", "ReceitasMercado.Api.dll"]
