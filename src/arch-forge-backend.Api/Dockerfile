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
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "arch-forge-backend.Api.dll"]
