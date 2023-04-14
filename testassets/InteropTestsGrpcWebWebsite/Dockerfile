FROM mcr.microsoft.com/dotnet/nightly/sdk:8.0 AS build-env
WORKDIR /app

# Copy everything
COPY . ./
RUN dotnet --info
RUN dotnet restore testassets/InteropTestsGrpcWebWebsite
RUN dotnet publish testassets/InteropTestsGrpcWebWebsite -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/nightly/aspnet:8.0
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "InteropTestsGrpcWebWebsite.dll"]