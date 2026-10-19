# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first to optimize layer caching
COPY ["TalentFlow.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/TalentFlow.Domain/TalentFlow.Domain.csproj", "src/TalentFlow.Domain/"]
COPY ["src/TalentFlow.Application/TalentFlow.Application.csproj", "src/TalentFlow.Application/"]
COPY ["src/TalentFlow.Infrastructure/TalentFlow.Infrastructure.csproj", "src/TalentFlow.Infrastructure/"]
COPY ["src/TalentFlow.Api/TalentFlow.Api.csproj", "src/TalentFlow.Api/"]
COPY ["tests/TalentFlow.Domain.Tests/TalentFlow.Domain.Tests.csproj", "tests/TalentFlow.Domain.Tests/"]
COPY ["tests/TalentFlow.Application.Tests/TalentFlow.Application.Tests.csproj", "tests/TalentFlow.Application.Tests/"]
COPY ["tests/TalentFlow.ArchitectureTests/TalentFlow.ArchitectureTests.csproj", "tests/TalentFlow.ArchitectureTests/"]
COPY ["tests/TalentFlow.Api.IntegrationTests/TalentFlow.Api.IntegrationTests.csproj", "tests/TalentFlow.Api.IntegrationTests/"]

RUN dotnet restore "TalentFlow.sln"

# Copy all sources and build
COPY . .
WORKDIR "/src/src/TalentFlow.Api"
RUN dotnet build "TalentFlow.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "TalentFlow.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create upload & data directories and set non-root app user ownership
RUN mkdir -p /app/uploads/resumes /app/data && chown -R $APP_UID:$APP_UID /app
USER $APP_UID

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "TalentFlow.Api.dll"]
