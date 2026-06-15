# WhatsApp Backup Service

A .NET 8 application to backup WhatsApp messages to PostgreSQL, with scheduled daily backups and manual backup support.

## Features

- ✅ Full and incremental backup support
- ✅ Scheduled daily backups (configurable cron)
- ✅ Manual backup via command line
- ✅ Media file download and storage
- ✅ PostgreSQL with proper indexing
- ✅ Full-text search support

## Prerequisites

- .NET 8 SDK
- PostgreSQL 14+
- OpenClaw gateway running with WhatsApp connected

## Quick Start

### 1. Clone and Build

```bash
cd WhatsAppBackup
dotnet restore
dotnet build
```

### 2. Configure

Edit `src/WhatsAppBackup/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "WhatsAppDb": "Host=your-host;Port=5432;Database=whatsapp_backup;Username=postgres;Password=your-password"
  },
  "OpenClaw": {
    "GatewayUrl": "http://localhost:18789",
    "GatewayToken": "your-token-if-needed"
  },
  "Backup": {
    "ScheduleCron": "0 0 3 * * ?",
    "MediaStoragePath": "./media",
    "EnableMediaDownload": true
  }
}
```

### 3. Initialize Database

Option A - Using EF migrations:
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Option B - Manual SQL:
```bash
psql -h your-host -U postgres -d whatsapp_backup -f src/WhatsAppBackup/Migrations/InitialCreate.sql
```

### 4. Run

**As a service (scheduled backups):**
```bash
dotnet run --project src/WhatsAppBackup
```

**Manual full backup:**
```bash
dotnet run --project src/WhatsAppBackup -- backup
```

**Manual incremental backup:**
```bash
dotnet run --project src/WhatsAppBackup -- incremental
```

## Commands

| Command | Description |
|---------|-------------|
| `(none)` | Run as daemon with scheduled backups |
| `backup` | Run full backup now |
| `incremental` | Run incremental backup (new messages only) |
| `migrate` | Run database migrations |
| `help` | Show help |

## Schedule Configuration

The `ScheduleCron` setting uses Quartz cron format:

| Expression | Description |
|------------|-------------|
| `0 0 3 * * ?` | Daily at 3 AM |
| `0 0 */6 * * ?` | Every 6 hours |
| `0 0 0 * * SUN` | Weekly on Sunday midnight |

## Database Schema

- **contacts** - WhatsApp contacts (phone, name, about)
- **chats** - DM and group conversations
- **messages** - All message content
- **media** - Attached media files
- **reactions** - Message reactions
- **polls** - Poll messages with options and votes
- **group_participants** - Group membership

## Running as a System Service

### Linux (systemd)

Create `/etc/systemd/system/whatsapp-backup.service`:

```ini
[Unit]
Description=WhatsApp Backup Service
After=network.target postgresql.service

[Service]
Type=simple
User=your-user
WorkingDirectory=/path/to/WhatsAppBackup
ExecStart=/usr/bin/dotnet run --project src/WhatsAppBackup
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable whatsapp-backup
sudo systemctl start whatsapp-backup
```

### Windows Service

Use NSSM or publish as a Windows Service.

## Logs

Logs are written to:
- Console (stdout)
- `logs/whatsapp-backup-YYYY-MM-DD.log`

## Note on OpenClaw Integration

This app is designed to work with OpenClaw's WhatsApp gateway. The `OpenClawClient` service expects API endpoints at:
- `GET /api/whatsapp/chats` - List all chats
- `GET /api/whatsapp/messages/{jid}` - Get messages for a chat

You may need to implement these endpoints in OpenClaw or modify the client to read from WhatsApp's local message store directly.

## License

MIT
