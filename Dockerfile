# Stage 1: Compile ps3iso-utils
FROM alpine:latest AS ps3tools
RUN apk add --no-cache git gcc musl-dev && \
    git clone https://github.com/bucanero/ps3iso-utils.git && \
    gcc -static ps3iso-utils/makeps3iso/makeps3iso.c -o /usr/local/bin/makeps3iso && \
    gcc -static ps3iso-utils/patchps3iso/patchps3iso.c -o /usr/local/bin/patchps3iso

# Stage 2: Build frontend
FROM oven/bun:alpine AS frontend
WORKDIR /src/VimmsDownloader/client
COPY VimmsDownloader/client/package.json VimmsDownloader/client/bun.lock ./
RUN bun install --frozen-lockfile
COPY VimmsDownloader/client/ ./
RUN bun run build

# Stage 3: Build .NET app
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src

COPY Modules/Module.Core/Module.Core.csproj Modules/Module.Core/
COPY Modules/Module.Extractor/Module.Extractor.csproj Modules/Module.Extractor/
COPY Modules/Module.Ps3IsoTools/Module.Ps3IsoTools.csproj Modules/Module.Ps3IsoTools/
COPY Modules/Module.Ps3Pipeline/Module.Ps3Pipeline.csproj Modules/Module.Ps3Pipeline/
COPY Modules/Module.Download/Module.Download.csproj Modules/Module.Download/
COPY Modules/Module.Sync/Module.Sync.csproj Modules/Module.Sync/
COPY VimmsDownloader/VimmsDownloader.csproj VimmsDownloader/
RUN dotnet restore VimmsDownloader/VimmsDownloader.csproj -r linux-x64

ARG VERSION=0.0.0-dev

COPY Modules/ Modules/
COPY VimmsDownloader/ VimmsDownloader/
COPY --from=frontend /src/VimmsDownloader/wwwroot/ VimmsDownloader/wwwroot/
RUN dotnet publish VimmsDownloader/VimmsDownloader.csproj -c Release -r linux-x64 -o /app /p:Version=${VERSION}

# Stage 4: Runtime
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
RUN apt-get update && apt-get install -y --no-install-recommends 7zip && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=ps3tools /usr/local/bin/makeps3iso /usr/local/bin/
COPY --from=ps3tools /usr/local/bin/patchps3iso /usr/local/bin/
COPY --from=build /app .

VOLUME /vimms

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__Default="Data Source=/vimms/data/queue.db"

EXPOSE 5000

ENTRYPOINT ["./VimmsDownloader"]
