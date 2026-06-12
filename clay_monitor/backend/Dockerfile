FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ClayMonitor.csproj .
RUN dotnet restore
COPY src/ ./src/
RUN dotnet publish src/Program.cs -c Release -o /app/publish /p:PublishSingleFile=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN addgroup --system clay && adduser --system --ingroup clay clay
COPY --from=build /app/publish .
RUN mkdir -p /app/data /app/logs && chown -R clay:clay /app
USER clay
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "ClayMonitor.dll"]
