# Stage 1: Compile ps3iso-utils
FROM alpine:latest AS ps3tools
RUN apk add --no-cache git gcc musl-dev && \
    git clone https://github.com/bucanero/ps3iso-utils.git && \
    gcc -static ps3iso-utils/makeps3iso/makeps3iso.c -o /usr/local/bin/makeps3iso && \
    gcc -static ps3iso-utils/patchps3iso/patchps3iso.c -o /usr/local/bin/patchps3iso

# Stage 2: Build .NET app
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src

COPY Ps3IsoTools/Ps3IsoTools.csproj Ps3IsoTools/
COPY ZipExtractor/ZipExtractor.csproj ZipExtractor/
COPY VimmsDownloader/VimmsDownloader.csproj VimmsDownloader/
RUN dotnet restore VimmsDownloader/VimmsDownloader.csproj -r linux-x64

COPY Ps3IsoTools/ Ps3IsoTools/
COPY ZipExtractor/ ZipExtractor/
COPY VimmsDownloader/ VimmsDownloader/
RUN dotnet publish VimmsDownloader/VimmsDownloader.csproj -c Release -r linux-x64 -o /app

# Stage 3: Runtime (non-chiseled — needs to run 7z, makeps3iso, patchps3iso)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
RUN apt-get update && apt-get install -y --no-install-recommends 7zip && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=ps3tools /usr/local/bin/makeps3iso /usr/local/bin/
COPY --from=ps3tools /usr/local/bin/patchps3iso /usr/local/bin/
COPY --from=build /app .

VOLUME /app/data
VOLUME /downloads

ENV ASPNETCORE_URLS=http://+:5000
ENV DownloadPath=/downloads
ENV ConnectionStrings__Default="Data Source=/app/data/queue.db"

EXPOSE 5000

ENTRYPOINT ["./VimmsDownloader"]
