FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ErrandRuns.slnx && dotnet publish src/ErrandRuns.Api/ErrandRuns.Api.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN adduser --disabled-password --gecos "" --uid 10001 appuser
USER appuser
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet","ErrandRuns.Api.dll"]
