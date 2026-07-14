# specdrift as a container: the deterministic spec/drift judge for ANY stack — the
# rules/drift profiles are data, so non-.NET repos run it too. Also carries the stdio
# MCP server (`specdrift mcp`) for agent runtimes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Specdrift/Specdrift.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /work
COPY --from=build /app /app
ENTRYPOINT ["dotnet", "/app/Specdrift.dll"]
