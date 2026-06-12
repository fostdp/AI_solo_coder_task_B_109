FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ClaySimulator.csproj .
RUN dotnet restore
COPY Program.cs .
RUN dotnet publish Program.cs -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV BACKEND_URL=http://backend:5000
ENV REPORT_INTERVAL_MINUTES=45
ENV FAST_MODE=false
ENV INJECT_HIGH_NA=6,21
ENTRYPOINT ["dotnet", "ClaySimulator.dll"]
