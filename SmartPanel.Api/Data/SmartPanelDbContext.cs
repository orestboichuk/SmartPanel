using Microsoft.EntityFrameworkCore;

namespace SmartPanel.Api.Data;

public class SmartPanelDbContext : DbContext
{
    public SmartPanelDbContext(DbContextOptions<SmartPanelDbContext> options) : base(options)
    {
    }

    public DbSet<DeviceRoomAssignmentEntity> DeviceRoomAssignments => Set<DeviceRoomAssignmentEntity>();
    public DbSet<DeviceRoomBindingEntity> DeviceRoomBindings => Set<DeviceRoomBindingEntity>();
    public DbSet<HomeAssistantEntitySnapshotEntity> HomeAssistantEntitySnapshots => Set<HomeAssistantEntitySnapshotEntity>();
    public DbSet<DeviceDisplayOverrideEntity> DeviceDisplayOverrides => Set<DeviceDisplayOverrideEntity>();
    public DbSet<DeviceTypeOverrideEntity> DeviceTypeOverrides => Set<DeviceTypeOverrideEntity>();
    public DbSet<DirectCameraEntity> DirectCameras => Set<DirectCameraEntity>();
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();
    public DbSet<AppRoleEntity> AppRoles => Set<AppRoleEntity>();
    public DbSet<AppUserRoleEntity> AppUserRoles => Set<AppUserRoleEntity>();
    public DbSet<DeviceRegistryEntity> DeviceRegistry => Set<DeviceRegistryEntity>();
    public DbSet<PanelLayoutEntity> PanelLayouts => Set<PanelLayoutEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceRoomAssignmentEntity>(entity =>
        {
            entity.ToTable("device_room_assignments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityId).HasMaxLength(255).IsRequired();
            entity.Property(x => x.RoomName).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.EntityId).IsUnique();
        });

        modelBuilder.Entity<DeviceRoomBindingEntity>(entity =>
        {
            entity.ToTable("device_room_bindings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.DeviceKey).HasColumnName("device_key").HasMaxLength(255).IsRequired();
            entity.Property(x => x.RoomName).HasColumnName("room_name").HasMaxLength(120).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.DeviceKey).IsUnique();
        });

        modelBuilder.Entity<HomeAssistantEntitySnapshotEntity>(entity =>
        {
            entity.ToTable("ha_entity_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceKey).HasColumnName("device_key").HasMaxLength(255).IsRequired();
            entity.Property(x => x.RegistryDeviceId).HasColumnName("registry_device_id").HasMaxLength(255);
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
            entity.Property(x => x.RoomName).HasColumnName("room_name").HasMaxLength(120);
            entity.Property(x => x.RawState).HasColumnName("raw_state").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
            entity.Property(x => x.NumericValue).HasColumnName("numeric_value");
            entity.Property(x => x.IsOnline).HasColumnName("is_online");
            entity.Property(x => x.AttributesJson).HasColumnName("attributes_json").HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.LastUpdatedUtc).HasColumnName("last_updated_utc");
            entity.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.HasIndex(x => x.EntityId).IsUnique();
            entity.HasIndex(x => x.DeviceKey);
            entity.HasIndex(x => x.RegistryDeviceId);
            entity.HasIndex(x => x.RoomName);
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<DeviceDisplayOverrideEntity>(entity =>
        {
            entity.ToTable("device_display_overrides");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PresentationKey).HasColumnName("presentation_key").HasMaxLength(255).IsRequired();
            entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(255).IsRequired();
            entity.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.DisplayNameOverride).HasColumnName("display_name_override").HasMaxLength(255);
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.PresentationKey).IsUnique();
        });

        modelBuilder.Entity<DeviceTypeOverrideEntity>(entity =>
        {
            entity.ToTable("device_type_overrides");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PresentationKey).HasColumnName("presentation_key").HasMaxLength(255).IsRequired();
            entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(255).IsRequired();
            entity.Property(x => x.OriginalType).HasColumnName("original_type").HasMaxLength(64).IsRequired();
            entity.Property(x => x.TypeOverride).HasColumnName("type_override").HasMaxLength(64);
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.PresentationKey).IsUnique();
        });

        modelBuilder.Entity<DirectCameraEntity>(entity =>
        {
            entity.ToTable("direct_cameras");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CameraId).HasColumnName("camera_id").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Port).HasColumnName("port");
            entity.Property(x => x.Scheme).HasColumnName("scheme").HasMaxLength(16).IsRequired();
            entity.Property(x => x.DeviceServiceUrl).HasColumnName("device_service_url").HasMaxLength(1024).IsRequired();
            entity.Property(x => x.MediaServiceUrl).HasColumnName("media_service_url").HasMaxLength(1024);
            entity.Property(x => x.MediaNamespace).HasColumnName("media_namespace").HasMaxLength(255);
            entity.Property(x => x.PtzServiceUrl).HasColumnName("ptz_service_url").HasMaxLength(1024);
            entity.Property(x => x.SnapshotUri).HasColumnName("snapshot_uri").HasMaxLength(2048);
            entity.Property(x => x.StreamUri).HasColumnName("stream_uri").HasMaxLength(2048);
            entity.Property(x => x.SnapshotUrlOverride).HasColumnName("snapshot_url_override").HasMaxLength(2048);
            entity.Property(x => x.MjpegUrlOverride).HasColumnName("mjpeg_url_override").HasMaxLength(2048);
            entity.Property(x => x.SelectedProfileToken).HasColumnName("selected_profile_token").HasMaxLength(255);
            entity.Property(x => x.PreferredLiveProfileToken).HasColumnName("preferred_live_profile_token").HasMaxLength(255);
            entity.Property(x => x.LiveProfileToken).HasColumnName("live_profile_token").HasMaxLength(255);
            entity.Property(x => x.LiveSnapshotUri).HasColumnName("live_snapshot_uri").HasMaxLength(2048);
            entity.Property(x => x.LiveStreamUri).HasColumnName("live_stream_uri").HasMaxLength(2048);
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(255);
            entity.Property(x => x.PasswordProtected).HasColumnName("password_protected").HasColumnType("TEXT");
            entity.Property(x => x.Manufacturer).HasColumnName("manufacturer").HasMaxLength(255);
            entity.Property(x => x.Model).HasColumnName("model").HasMaxLength(255);
            entity.Property(x => x.FirmwareVersion).HasColumnName("firmware_version").HasMaxLength(255);
            entity.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(255);
            entity.Property(x => x.HardwareId).HasColumnName("hardware_id").HasMaxLength(255);
            entity.Property(x => x.ProfilesJson).HasColumnName("profiles_json").HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.Online).HasColumnName("online");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasColumnType("TEXT");
            entity.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.CameraId).IsUnique();
            entity.HasIndex(x => x.Host);
        });

        modelBuilder.Entity<AppUserEntity>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
            entity.Property(x => x.PasswordSalt).HasColumnName("password_salt").HasMaxLength(512).IsRequired();
            entity.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<AppRoleEntity>(entity =>
        {
            entity.ToTable("app_roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<AppUserRoleEntity>(entity =>
        {
            entity.ToTable("app_user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceRegistryEntity>(entity =>
        {
            entity.ToTable("device_registry");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(255).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.RoomName).HasColumnName("room_name").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsVisible).HasColumnName("is_visible");
            entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.EntityId).IsUnique();
            entity.HasIndex(x => x.RoomName);
        });

        modelBuilder.Entity<PanelLayoutEntity>(entity =>
        {
            entity.ToTable("panel_layouts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Route).HasColumnName("route").HasMaxLength(120).IsRequired();
            entity.Property(x => x.LayoutJson).HasColumnName("layout_json").HasColumnType("TEXT").IsRequired();
            entity.Property(x => x.IsDefault).HasColumnName("is_default");
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(x => x.Route).IsUnique();
        });
    }
}
