using Microsoft.EntityFrameworkCore;
using SmartPanel.Api.Auth;

namespace SmartPanel.Api.Data;

public static class SmartPanelDbBootstrapper
{
    private static readonly string[] DevicePartSuffixes =
    [
        "_temperature",
        "_humidity",
        "_battery",
        "_moisture",
        "_door",
        "_motion"
    ];

    public static async Task InitializeAsync(SmartPanelDbContext db, AdminBootstrapOptions adminBootstrap)
    {
        await db.Database.EnsureCreatedAsync();

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS device_room_bindings (
    id INTEGER NOT NULL CONSTRAINT PK_device_room_bindings PRIMARY KEY AUTOINCREMENT,
    device_key TEXT NOT NULL,
    room_name TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_device_room_bindings_device_key ON device_room_bindings (device_key);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ha_entity_snapshots (
    id INTEGER NOT NULL CONSTRAINT PK_ha_entity_snapshots PRIMARY KEY AUTOINCREMENT,
    entity_id TEXT NOT NULL,
    domain TEXT NOT NULL,
    device_key TEXT NOT NULL,
    registry_device_id TEXT NULL,
    display_name TEXT NOT NULL,
    type TEXT NOT NULL,
    room_name TEXT NULL,
    raw_state TEXT NOT NULL,
    unit TEXT NOT NULL,
    numeric_value REAL NULL,
    is_online INTEGER NOT NULL,
    attributes_json TEXT NOT NULL,
    capabilities_json TEXT NOT NULL,
    last_updated_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    is_active INTEGER NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_ha_entity_snapshots_entity_id ON ha_entity_snapshots (entity_id);");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_ha_entity_snapshots_device_key ON ha_entity_snapshots (device_key);");
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ha_entity_snapshots ADD COLUMN registry_device_id TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_ha_entity_snapshots_registry_device_id ON ha_entity_snapshots (registry_device_id);");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_ha_entity_snapshots_room_name ON ha_entity_snapshots (room_name);");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_ha_entity_snapshots_is_active ON ha_entity_snapshots (is_active);");

        await MigrateLegacyRoomAssignmentsAsync(db);
        await EnsureIdentityAndAdminAsync(db, adminBootstrap);
        await EnsureAdminTablesAsync(db);
        await EnsureDirectCameraTablesAsync(db);
    }

    private static async Task MigrateLegacyRoomAssignmentsAsync(SmartPanelDbContext db)
    {
        var legacyAssignments = await db.DeviceRoomAssignments
            .AsNoTracking()
            .ToListAsync();

        if (legacyAssignments.Count == 0)
        {
            return;
        }

        var existingBindings = await db.DeviceRoomBindings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.DeviceKey, StringComparer.OrdinalIgnoreCase);

        foreach (var legacy in legacyAssignments)
        {
            var deviceKey = BuildDeviceKey(legacy.EntityId);
            if (string.IsNullOrWhiteSpace(deviceKey) || existingBindings.ContainsKey(deviceKey))
            {
                continue;
            }

            db.DeviceRoomBindings.Add(new DeviceRoomBindingEntity
            {
                DeviceKey = deviceKey,
                RoomName = legacy.RoomName.Trim(),
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static string BuildDeviceKey(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return string.Empty;
        }

        var dotIndex = entityId.IndexOf('.');
        var key = dotIndex >= 0 && dotIndex < entityId.Length - 1 ? entityId[(dotIndex + 1)..] : entityId;

        foreach (var suffix in DevicePartSuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^suffix.Length];
            }
        }

        return key.Trim().ToLowerInvariant();
    }

    private static async Task EnsureIdentityAndAdminAsync(SmartPanelDbContext db, AdminBootstrapOptions adminBootstrap)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS app_roles (
    id INTEGER NOT NULL CONSTRAINT PK_app_roles PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_roles_name ON app_roles (name);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS app_users (
    id INTEGER NOT NULL CONSTRAINT PK_app_users PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    is_active INTEGER NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_users_username ON app_users (username);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS app_user_roles (
    user_id INTEGER NOT NULL,
    role_id INTEGER NOT NULL,
    CONSTRAINT PK_app_user_roles PRIMARY KEY (user_id, role_id),
    CONSTRAINT FK_app_user_roles_app_users_user_id FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE,
    CONSTRAINT FK_app_user_roles_app_roles_role_id FOREIGN KEY (role_id) REFERENCES app_roles (id) ON DELETE CASCADE
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_app_user_roles_role_id ON app_user_roles (role_id);");

        var adminRole = await db.AppRoles.FirstOrDefaultAsync(x => x.Name == "Admin");
        if (adminRole is null)
        {
            adminRole = new AppRoleEntity { Name = "Admin" };
            db.AppRoles.Add(adminRole);
        }

        var clientRole = await db.AppRoles.FirstOrDefaultAsync(x => x.Name == "Client");
        if (clientRole is null)
        {
            clientRole = new AppRoleEntity { Name = "Client" };
            db.AppRoles.Add(clientRole);
        }

        await db.SaveChangesAsync();

        var adminUsername = string.IsNullOrWhiteSpace(adminBootstrap.Username) ? "admin" : adminBootstrap.Username.Trim();
        var adminPassword = string.IsNullOrWhiteSpace(adminBootstrap.Password) ? "admin12345" : adminBootstrap.Password;
        var adminUser = await db.AppUsers.FirstOrDefaultAsync(x => x.Username == adminUsername);

        if (adminUser is null)
        {
            var (hash, salt) = PasswordHasher.HashPassword(adminPassword);
            adminUser = new AppUserEntity
            {
                Username = adminUsername,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.AppUsers.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var hasAdminRole = await db.AppUserRoles.AnyAsync(x => x.UserId == adminUser.Id && x.RoleId == adminRole.Id);
        if (!hasAdminRole)
        {
            db.AppUserRoles.Add(new AppUserRoleEntity
            {
                UserId = adminUser.Id,
                RoleId = adminRole.Id
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureAdminTablesAsync(SmartPanelDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS device_registry (
    id INTEGER NOT NULL CONSTRAINT PK_device_registry PRIMARY KEY AUTOINCREMENT,
    entity_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    room_name TEXT NOT NULL,
    type TEXT NOT NULL,
    is_visible INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_device_registry_entity_id ON device_registry (entity_id);");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_device_registry_room_name ON device_registry (room_name);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS panel_layouts (
    id INTEGER NOT NULL CONSTRAINT PK_panel_layouts PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    route TEXT NOT NULL,
    layout_json TEXT NOT NULL,
    is_default INTEGER NOT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_panel_layouts_route ON panel_layouts (route);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS device_display_overrides (
    id INTEGER NOT NULL CONSTRAINT PK_device_display_overrides PRIMARY KEY AUTOINCREMENT,
    presentation_key TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    original_name TEXT NOT NULL,
    display_name_override TEXT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_device_display_overrides_presentation_key ON device_display_overrides (presentation_key);");
    }

    private static async Task EnsureDirectCameraTablesAsync(SmartPanelDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS direct_cameras (
    id INTEGER NOT NULL CONSTRAINT PK_direct_cameras PRIMARY KEY AUTOINCREMENT,
    camera_id TEXT NOT NULL,
    name TEXT NOT NULL,
    host TEXT NOT NULL,
    port INTEGER NOT NULL,
    scheme TEXT NOT NULL,
    device_service_url TEXT NOT NULL,
    media_service_url TEXT NULL,
    media_namespace TEXT NULL,
    ptz_service_url TEXT NULL,
    snapshot_uri TEXT NULL,
    stream_uri TEXT NULL,
    snapshot_url_override TEXT NULL,
    mjpeg_url_override TEXT NULL,
    selected_profile_token TEXT NULL,
    preferred_live_profile_token TEXT NULL,
    live_profile_token TEXT NULL,
    live_snapshot_uri TEXT NULL,
    live_stream_uri TEXT NULL,
    username TEXT NULL,
    password_protected TEXT NULL,
    manufacturer TEXT NULL,
    model TEXT NULL,
    firmware_version TEXT NULL,
    serial_number TEXT NULL,
    hardware_id TEXT NULL,
    profiles_json TEXT NOT NULL,
    capabilities_json TEXT NOT NULL,
    online INTEGER NOT NULL,
    last_error TEXT NULL,
    last_seen_utc TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_direct_cameras_camera_id ON direct_cameras (camera_id);");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS IX_direct_cameras_host ON direct_cameras (host);");

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN snapshot_url_override TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN mjpeg_url_override TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN preferred_live_profile_token TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN live_profile_token TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN live_snapshot_uri TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE direct_cameras ADD COLUMN live_stream_uri TEXT NULL;");
        }
        catch
        {
            // Column already exists.
        }
    }
}
