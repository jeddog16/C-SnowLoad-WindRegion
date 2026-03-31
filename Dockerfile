FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AhdApi.csproj", "./"]
RUN dotnet restore "./AhdApi.csproj"

COPY . .
RUN dotnet build "AhdApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AhdApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

RUN mkdir -p /app/Data

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "AhdApi.dll"]
