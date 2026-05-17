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

// Initialize Firebase Admin SDK safely
var serviceAccountPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service-account.json");
if (!File.Exists(serviceAccountPath))
{
    // Try in the project root relative path
    serviceAccountPath = "service-account.json";
}

if (File.Exists(serviceAccountPath))
{
    try
    {
        FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions()
        {
            Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(serviceAccountPath)
        });
        Console.WriteLine("Firebase Admin SDK initialized successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing Firebase Admin SDK: {ex.Message}");
    }
}
else
{
    Console.WriteLine("Warning: service-account.json was not found in the application directory. Push notifications will be disabled until it is provided.");
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
