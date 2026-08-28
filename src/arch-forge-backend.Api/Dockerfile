FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/arch-forge-backend.Domain/arch-forge-backend.Domain.csproj", "src/arch-forge-backend.Domain/"]
COPY ["src/arch-forge-backend.Application/arch-forge-backend.Application.csproj", "src/arch-forge-backend.Application/"]
COPY ["src/arch-forge-backend.Infrastructure/arch-forge-backend.Infrastructure.csproj", "src/arch-forge-backend.Infrastructure/"]
COPY ["src/arch-forge-backend.Api/arch-forge-backend.Api.csproj", "src/arch-forge-backend.Api/"]
RUN dotnet restore "src/arch-forge-backend.Api/arch-forge-backend.Api.csproj"

COPY . .
RUN dotnet publish "src/arch-forge-backend.Api/arch-forge-backend.Api.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# The runtime image ships no HTTP client, and the compose healthcheck needs one to ask `/api/health`
# whether the database is still there. ~4 MB, and it is the difference between a container that is
# "up" and one that is answering.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "arch-forge-backend.Api.dll"]
