using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartPanel.Api.Auth;
using SmartPanel.Api.Data;
using SmartPanel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection();
builder.Services.AddHttpClient<HomeAssistantClient>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection(AdminBootstrapOptions.SectionName));
builder.Services.Configure<CameraTranscodingOptions>(builder.Configuration.GetSection(CameraTranscodingOptions.SectionName));
builder.Services.Configure<MediaMtxOptions>(builder.Configuration.GetSection(MediaMtxOptions.SectionName));
builder.Services.AddDbContext<SmartPanelDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SmartPanelDb")
                           ?? "Data Source=smartpanel.db";
    options.UseSqlite(connectionString);
});
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<MediaMtxService>();
builder.Services.AddScoped<IDirectCameraService, DirectCameraService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<ISmartHomeService, HomeAssistantSmartHomeService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmartPanelDbContext>();
    var adminBootstrap = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminBootstrapOptions>>()
        .Value;
    await SmartPanelDbBootstrapper.InitializeAsync(db, adminBootstrap);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
