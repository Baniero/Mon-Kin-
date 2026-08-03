# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["MonKineBlazor.sln", "./"]
COPY ["MonKineBlazor.Server/MonKineBlazor.Server.csproj", "MonKineBlazor.Server/"]
COPY ["MonKineBlazor.Client/MonKineBlazor.Client.csproj", "MonKineBlazor.Client/"]
COPY ["MonKineBlazor.Shared/MonKineBlazor.Shared.csproj", "MonKineBlazor.Shared/"]

# Restore dependencies
RUN dotnet restore "MonKineBlazor.Server/MonKineBlazor.Server.csproj"

# Copy source and build client
COPY . .
WORKDIR /src/MonKineBlazor.Client
RUN dotnet publish -c Release -o /tmp/clientpublish

# Prepare server and publish
WORKDIR /src/MonKineBlazor.Server
RUN mkdir -p wwwroot
RUN cp -r /tmp/clientpublish/wwwroot/* wwwroot/
RUN dotnet publish "MonKineBlazor.Server.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "MonKineBlazor.Server.dll"]
