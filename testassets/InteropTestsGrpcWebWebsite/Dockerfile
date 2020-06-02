FROM mcr.microsoft.com/dotnet/core/sdk:3.1.300 AS build-env
WORKDIR /app

# Copy everything
COPY . ./
RUN dotnet restore testassets/InteropTestsGrpcWebWebsite
RUN dotnet publish testassets/InteropTestsGrpcWebWebsite -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "InteropTestsGrpcWebWebsite.dll"]