FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VimmsDownloader/VimmsDownloader.csproj VimmsDownloader/
RUN dotnet restore VimmsDownloader/VimmsDownloader.csproj

COPY VimmsDownloader/ VimmsDownloader/
RUN dotnet publish VimmsDownloader/VimmsDownloader.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app .

# Database lives here (mount as volume to persist)
VOLUME /app/data

# Downloads go here (mount to host folder)
VOLUME /downloads

ENV ASPNETCORE_URLS=http://+:5000
ENV DownloadPath=/downloads

# SQLite db in the data volume
ENV ConnectionStrings__Default="Data Source=/app/data/queue.db"

EXPOSE 5000

ENTRYPOINT ["dotnet", "VimmsDownloader.dll"]
