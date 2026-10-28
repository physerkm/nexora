#!/bin/sh
# Database backups for docker-compose.prod.yml: a pg_dump every BACKUP_INTERVAL_HOURS into /backups,
# keeping the last BACKUP_KEEP_DAYS days. Restore steps: docs/deployment.md.
set -eu

interval_hours="${BACKUP_INTERVAL_HOURS:-24}"
keep_days="${BACKUP_KEEP_DAYS:-14}"

while true; do
    file="/backups/nexora-$(date -u +%Y%m%d-%H%M%S).dump"

    # Write to a temporary name first so a failed dump never looks like a valid backup.
    if pg_dump --format=custom --file="$file.partial"; then
        mv "$file.partial" "$file"
        echo "backup: wrote $file ($(du -h "$file" | cut -f1))"
    else
        rm -f "$file.partial"
        echo "backup: pg_dump failed" >&2
    fi

    find /backups -name 'nexora-*.dump' -mtime +"$keep_days" -delete
    sleep $((interval_hours * 3600))
done
