using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using SocialPublisher.API.Data;
using SocialPublisher.API.Services;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// JWT + Google OAuth Authentication configuration
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        var jwtKey = cfg["Jwt:Key"] ?? "a-very-secret-encryption-key-for-wiseliy-social-media-jwt-tokens-at-least-32-chars";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = cfg["Jwt:Issuer"] ?? "wiseliy",
            ValidateAudience = true,
            ValidAudience = cfg["Jwt:Audience"] ?? "wiseliy-social",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    })
    // short-lived cookie used only during the Google handshake
    .AddCookie("External", o =>
    {
        o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddGoogle(o =>
    {
        o.SignInScheme = "External";
        o.ClientId = cfg["Google:ClientId"] ?? "YOUR_GOOGLE_CLIENT_ID_HERE";
        o.ClientSecret = cfg["Google:ClientSecret"] ?? "YOUR_GOOGLE_CLIENT_SECRET_HERE";
        o.CallbackPath = "/signin-google"; // must match the URI in Google Console
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Scope.Add("email");
        o.Scope.Add("profile");
        // Always show Google's "Choose an account" screen (fresh start after logout)
        o.Events.OnRedirectToAuthorizationEndpoint = ctx =>
        {
            ctx.Response.Redirect(QueryHelpers.AddQueryString(ctx.RedirectUri, "prompt", "select_account"));
            return Task.CompletedTask;
        };
    })
    .AddFacebook(o =>
    {
        o.SignInScheme = "External";
        o.AppId = cfg["SocialMedia:Facebook:ClientId"] ?? "";
        o.AppSecret = cfg["SocialMedia:Facebook:ClientSecret"] ?? "";
        o.CallbackPath = "/signin-facebook";
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Scope.Add("email");
        o.Scope.Add("public_profile");
        // Always ask Facebook to confirm / re-enter the account (fresh start after logout)
        o.Events.OnRedirectToAuthorizationEndpoint = ctx =>
        {
            ctx.Response.Redirect(QueryHelpers.AddQueryString(ctx.RedirectUri, "auth_type", "reauthenticate"));
            return Task.CompletedTask;
        };
    })
    .AddOAuth("LinkedIn", o =>
    {
        o.SignInScheme = "External";
        o.ClientId = cfg["SocialMedia:LinkedIn:ClientId"] ?? "";
        o.ClientSecret = cfg["SocialMedia:LinkedIn:ClientSecret"] ?? "";
        o.CallbackPath = "/signin-linkedin";
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.AuthorizationEndpoint = "https://www.linkedin.com/oauth/v2/authorization";
        o.TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";
        o.UserInformationEndpoint = "https://api.linkedin.com/v2/userinfo";
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
        o.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        o.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
        o.ClaimActions.MapJsonKey("picture", "picture");
        o.Events.OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
            var response = await context.Backchannel.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            context.RunClaimActions(user.RootElement);
        };
    });

builder.Services.AddAuthorization();

// Data protection for token encryption
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ITokenProtector, TokenProtector>();
builder.Services.AddScoped<TokenService>();

// Register HTTP Clients and Publishers
builder.Services.AddHttpClient<FacebookPublisher>();
builder.Services.AddScoped<ISocialPublisher, FacebookPublisher>();
builder.Services.AddHttpClient<InstagramPublisher>();
builder.Services.AddScoped<ISocialPublisher, InstagramPublisher>();
builder.Services.AddHttpClient<LinkedInPublisher>();
builder.Services.AddScoped<ISocialPublisher, LinkedInPublisher>();
builder.Services.AddHttpClient<PinterestPublisher>();
builder.Services.AddScoped<ISocialPublisher, PinterestPublisher>();
builder.Services.AddHttpClient<YouTubePublisher>();
builder.Services.AddScoped<ISocialPublisher, YouTubePublisher>();
builder.Services.AddHttpClient<XPublisher>();
builder.Services.AddScoped<ISocialPublisher, XPublisher>();
builder.Services.AddScoped<SocialPublishingService>();
builder.Services.AddHostedService<PostSchedulerBackgroundService>();
builder.Services.AddScoped<ISocialAnalyticsService, SocialAnalyticsService>();
builder.Services.AddScoped<ISocialEngagementService, SocialEngagementService>();
builder.Services.AddScoped<ISocialDiscoveryService, SocialDiscoveryService>();

// Google Ads integration
builder.Services.AddHttpClient<GoogleAdsService>();
builder.Services.AddScoped<IGoogleAdsService, GoogleAdsService>();

// Add DbContext (PostgreSQL)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Wiseliy Social Publisher API",
        Version = "v1",
        Description = "API for publishing posts to social media platforms"
    });
});

// CORS - allow frontend opened from file:// or any localhost origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Auto-apply migrations and seed social accounts from config on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    db.Database.Migrate();

    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE \"SocialAccounts\" ALTER COLUMN \"AvatarUrl\" TYPE text;");
    }
    catch { /* Ignore if already altered */ }

    // Seed default client workspace if none exists (for multi-tenant support)
    var defaultClient = db.SocialClients.FirstOrDefault();
    if (defaultClient == null)
    {
        defaultClient = new SocialPublisher.API.Models.SocialClient
        {
            Name = "Default Agency Workspace",
            Description = "Primary agency workspace for social accounts",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.SocialClients.Add(defaultClient);
        db.SaveChanges();
    }

    // Make sure there is one account row per platform (so foreign keys work).
    // IMPORTANT: a token from appsettings.json is only used as a FIRST-TIME seed.
    // Once a token has been saved (for example by the "connect" flow), it is never overwritten here.
    var platforms = new[] { "facebook", "instagram", "linkedin", "pinterest", "youtube", "x" };
    foreach (var platform in platforms)
    {
        var key = char.ToUpper(platform[0]) + platform.Substring(1);
        var token = config[$"SocialMedia:{key}:AccessToken"];
        var pageId = config[$"SocialMedia:{key}:PageId"]
                  ?? config[$"SocialMedia:{key}:AccountId"]
                  ?? config[$"SocialMedia:{key}:CompanyId"]
                  ?? config[$"SocialMedia:{key}:BoardId"];

        var isConnected = !string.IsNullOrEmpty(token) && token != "YOUR_ACCESS_TOKEN_HERE";

        var existing = db.SocialAccounts.FirstOrDefault(a => a.Platform == platform);
        if (existing == null)
        {
            db.SocialAccounts.Add(new SocialPublisher.API.Models.SocialAccount
            {
                Platform = platform,
                AccountName = $"{key} Account",
                ExternalAccountId = pageId ?? "",
                AccessToken = token ?? "",
                IsConnected = isConnected,
                ClientId = defaultClient.Id
            });
        }
        else
        {
            if (existing.ClientId == null)
                existing.ClientId = defaultClient.Id;

            if (!string.IsNullOrEmpty(token) && string.IsNullOrEmpty(existing.AccessToken))
            {
                // Only fill in a token when the account has none yet.
                existing.AccessToken = token;
                if (string.IsNullOrWhiteSpace(existing.ExternalAccountId))
                    existing.ExternalAccountId = pageId ?? "";
                existing.IsConnected = isConnected;
            }
        }
    }
    db.SaveChanges();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Wiseliy Social Publisher API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowAll");

// ── Serve the web page ───────────────────────────────────────────────────────────
// The API looks for index.html in these places (first match wins):
//   1) <project folder>\wwwroot
//   2) a folder named "frontend" in the project folder or in any folder above it
var webCandidates = new List<string> { Path.Combine(builder.Environment.ContentRootPath, "wwwroot") };
DirectoryInfo? searchDir = new DirectoryInfo(builder.Environment.ContentRootPath);
for (var level = 0; level < 6 && searchDir != null; level++, searchDir = searchDir.Parent)
    webCandidates.Add(Path.Combine(searchDir.FullName, "frontend"));

var webFolder = webCandidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html")));

if (webFolder != null)
{
    app.Logger.LogInformation("Serving the web page from: {Folder}", webFolder);

    var webFiles = new PhysicalFileProvider(webFolder);
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && !path.StartsWith("/api") && !path.StartsWith("/swagger") && !path.StartsWith("/uploads") && !path.StartsWith("/signin-google") && !path.StartsWith("/signin-linkedin") && !path.StartsWith("/signin-facebook") && !path.StartsWith("/api/googleads"))
        {
            // If path has no extension, check if an html file exists
            if (!Path.HasExtension(path))
            {
                var trimmed = path.Trim('/');
                var candidate = Path.Combine(webFolder, trimmed + ".html");
                if (File.Exists(candidate))
                {
                    context.Request.Path = "/" + trimmed + ".html";
                }
            }
        }
        await next();
    });
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });
}
else
{
    app.Logger.LogWarning("index.html was not found. Checked: {Folders}", string.Join(" | ", webCandidates));
}

// Serve uploaded images and videos publicly at /uploads/... (Instagram, Facebook and LinkedIn download media from here)
var uploadsPath = UploadPaths.GetFolder(builder.Environment.ContentRootPath);
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = UploadPaths.RequestPath
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// TEMPORARY helper: open http://localhost:5293/debug/webroot to see where the API looks for index.html.
// Only available in Development now.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/debug/webroot", (IWebHostEnvironment env) => new
    {
        contentRoot = env.ContentRootPath,
        servingPageFrom = webFolder,
        indexHtmlFound = webFolder != null,
        foldersChecked = webCandidates
    });
}

// If index.html was NOT found anywhere, show a clear message at "/" instead of a blank 404 page.
app.Use(async (context, next) =>
{
    if (webFolder == null && context.Request.Path == "/")
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(
            "<h3>index.html was not found</h3>" +
            "<p>Put <b>index.html</b> in one of these folders, then restart the API:</p><ul>" +
            string.Join("", webCandidates.Select(c => "<li>" + System.Net.WebUtility.HtmlEncode(c) + "</li>")) +
            "</ul><p>Swagger is available at <a href='/swagger'>/swagger</a>.</p>");
        return;
    }

    await next();
});

app.Run();