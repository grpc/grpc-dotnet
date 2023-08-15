FROM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS build-env
WORKDIR /app

# Copy everything
COPY . ./
RUN dotnet --info
RUN dotnet restore examples/Container/Backend
RUN dotnet publish examples/Container/Backend -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/nightly/aspnet:8.0-preview
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "Backend.dll"]