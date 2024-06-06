using App;
using App.Consumers;
using App.Context.Models;
using App.Middlewares;
using App.Services;
using dotenv.net;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
Mapper.BindMaps();
// Add Service Defaults
builder.AddServiceDefaults();
builder.AddMongoDBClient("mongodb");

// Register custom services
builder.Services.AddScoped<IMongoDbContext, MongoDbContext>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var databaseName = "AdminParkSharing"; // Ensure this is configured in your settings
    return new MongoDbContext(client, databaseName);
});

builder.Services.AddScoped<DebugSeedData>(); // Register SeedData service

builder.ConfigureMassTransit(config.GetConnectionString("rabbitmq"), 
    typeof(ReservationConsumer)
    );

// Add Configuration
builder.Host.ConfigureAppConfiguration((configBuilder) =>
{
    configBuilder.Sources.Clear();
    DotEnv.Load();
    configBuilder.AddEnvironmentVariables();
});

// Configure Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
});

// Add Services to the Container
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IParkingSpotService, ParkingSpotServiceMongo>();
builder.Services.AddControllers();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .WithHeaders(new string[] {
                HeaderNames.ContentType,
                HeaderNames.Authorization,
              })
              .AllowAnyMethod()
              .SetPreflightMaxAge(TimeSpan.FromSeconds(86400));
    });
});

// Configure Authentication
builder.Host.ConfigureServices((services) =>
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var audience = builder.Configuration.GetValue<string>("AUTH0_AUDIENCE");
            options.Authority = $"https://{builder.Configuration.GetValue<string>("AUTH0_DOMAIN")}/";
            options.Audience = audience;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuerSigningKey = true
            };
        })
);

var app = builder.Build();

#if DEBUG
#if DEBUG
using (var scope = app.Services.CreateScope())
{
    var seedData = scope.ServiceProvider.GetRequiredService<DebugSeedData>();
    var bus = scope.ServiceProvider.GetRequiredService<IBusControl>();
    await bus.StartAsync();
    await bus.StopAsync();
    await seedData.InitializeAsync();
}
#endif
#endif

// Validate Configuration Variables
var requiredVars = new string[] {
    "PORT",
    "CLIENT_ORIGIN_URL",
    "AUTH0_DOMAIN",
    "AUTH0_AUDIENCE",
};

foreach (var key in requiredVars)
{
    var value = app.Configuration.GetValue<string>(key);

    if (string.IsNullOrEmpty(value))
    {
        throw new Exception($"Config variable missing: {key}.");
    }
}

app.Urls.Add($"http://+:{app.Configuration.GetValue<string>("PORT")}");

// Middleware Configuration
app.UseErrorHandler();
app.UseSecureHeaders();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();