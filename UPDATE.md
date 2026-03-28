# Updating VIMM // DL

## Before you update

Your **database** and **downloads** live under the `/vimms` volume. If you mounted it, your data is safe. If you didn't, **updating the container will wipe everything**.

Check your setup:

```bash
docker inspect vimm-dl --format '{{range .Mounts}}{{.Destination}} → {{.Source}}{{"\n"}}{{end}}'
```

You should see `/vimms` mapped to a host path. If it's missing, back up first:

```bash
docker cp vimm-dl:/vimms ./vimms-backup
```

## Migrating from old volume layout

If you're upgrading from the old two-volume layout (`/app/data` + `/downloads`), consolidate to the new single volume:

```bash
# On your host, create the unified directory
mkdir -p ~/vimm/data ~/vimm/downloads

# Copy your existing data
cp /path/to/old/data/queue.db ~/vimm/data/
cp -r /path/to/old/downloads/* ~/vimm/downloads/

# Start with the new volume
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

> The old layout (`/app/data` + `/downloads`) still works for backward compatibility. The app checks for `/vimms/downloads` first, then falls back to `/downloads`.

## Docker (recommended)

Pull the latest image and recreate the container:

```bash
# Stop and remove the old container
docker stop vimm-dl
docker rm vimm-dl

# Pull the latest image
docker pull ghcr.io/eduvhc/vimm-dl:latest

# Start with the same volume
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Your queue, history, settings, events, and downloaded files all persist through the `/vimms` volume.

### Docker Compose

```yaml
services:
  vimm-dl:
    image: ghcr.io/eduvhc/vimm-dl:latest
    ports:
      - "5000:5000"
    volumes:
      - ~/vimm:/vimms
```

```bash
docker compose pull
docker compose up -d
```

### Updating to a specific version

Replace `latest` with a version tag:

```bash
docker pull ghcr.io/eduvhc/vimm-dl:v0.6.0
```

## Bare metal

```bash
git pull
cd VimmsDownloader/client && bun install && bun run build && cd ../..
dotnet publish VimmsDownloader/VimmsDownloader.csproj -c Release -r linux-x64 -o ./publish
```

The database (`queue.db`) stays in the working directory. No data is lost.

## Database migrations

Migrations run automatically on startup. The `schema_migrations` table tracks which migrations have been applied. No manual steps needed — just start the new version.

## Volume layout

```
/vimms/
├── data/
│   └── queue.db          ← SQLite database (queue, history, settings, events)
└── downloads/
    ├── downloading/      ← Partial files (auto-resume on restart)
    ├── completed/        ← Archives and converted ISOs
    └── ps3_temp/         ← Temporary conversion files (auto-cleaned)
```

Without the `/vimms` mount, **all data is lost** on container update.

## Changelog

See the [Releases](https://github.com/eduvhc/vimm-dl/releases) page for version history and changelogs.

## Troubleshooting

**Container won't start after update**: Check logs with `docker logs vimm-dl`. Database migrations should handle schema changes automatically.

**Missing data after update**: You didn't bind `/vimms`. Restore from backup if you have one:
```bash
docker cp ./vimms-backup/data/queue.db vimm-dl:/vimms/data/queue.db
docker restart vimm-dl
```

**Downgrading**: Not officially supported. Database migrations are forward-only. Back up `queue.db` before updating if you want a rollback path.
