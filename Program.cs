using dotnet_Learn.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddSignalR();

// Initialize Firebase Admin SDK safely (Support both environment variable and local file)
var firebaseConfigJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");

if (!string.IsNullOrEmpty(firebaseConfigJson))
{
    try
    {
        FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions()
        {
            Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(firebaseConfigJson)
        });
        Console.WriteLine("Firebase Admin SDK initialized successfully from environment variable.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing Firebase Admin SDK from environment variable: {ex.Message}");
    }
}
else
{
    // Try to find the service account file in standard local and Render secret file paths
    string[] candidatePaths = new string[]
    {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service-account.json"),
        "service-account.json",
        "/etc/secrets/service-account.json",
        "/etc/secrets/FIREBASE_SERVICE_ACCOUNT_JSON"
    };

    string? serviceAccountPath = null;
    foreach (var path in candidatePaths)
    {
        if (File.Exists(path))
        {
            serviceAccountPath = path;
            break;
        }
    }

    if (serviceAccountPath != null)
    {
        try
        {
            FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions()
            {
                Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(serviceAccountPath)
            });
            Console.WriteLine($"Firebase Admin SDK initialized successfully from local file at: {serviceAccountPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing Firebase Admin SDK from file at {serviceAccountPath}: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("Warning: Firebase credentials not found in environment variables, local files, or Render /etc/secrets/ paths. Push notifications are disabled.");
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials()
               .SetIsOriginAllowed(_ => true); // Allows any origin
    });
});

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, // Set to false for now to fix the 401 error
        ValidateAudience = false, // Set to false for now to fix the 401 error
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!)),
        ClockSkew = TimeSpan.Zero // Removes the 5-minute default delay
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, CustomUserIdProvider>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<dotnet_Learn.Hubs.ChatHub>("/chatHub");

app.Run();

public class CustomUserIdProvider : Microsoft.AspNetCore.SignalR.IUserIdProvider
{
    public string? GetUserId(Microsoft.AspNetCore.SignalR.HubConnectionContext connection)
    {
        return connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
               ?? connection.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
               ?? connection.User?.FindFirst("sub")?.Value;
    }
}
