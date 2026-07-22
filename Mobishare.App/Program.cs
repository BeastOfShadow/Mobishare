using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mobishare.App.Http;
using AutoMapper;
using Mobishare.Core.Data;
using Mobishare.Core.Security;
using Mobishare.Core.Security.Policies;
using Mobishare.Core.Requests;
using Mobishare.Infrastructure.Services.HostedServices;
using System.Reflection;
using Mobishare.Infrastructure.Services.MQTT;
using Mobishare.App.Services;
using Mobishare.Core.Services.GoogleGeocoding;
using Mobishare.Infrastructure.Services.SignalR;
using PayPal.REST.Client;
using PayPal.REST.Models;
using Mobishare.Ai.ChatBotAIService;
using Mobishare.Ai.ChatBotAIService.IntentRouter;
using Mobishare.Ai.ChatBotAIService.IntentClassifier;
using Mobishare.Ai.ChatBotAIService.ToolExecutor;
using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools.VehicleTools;
using Mobishare.Core.Services.UserContext;
using Mobishare.Ai.ChatBotAIService.ToolExecutor.Tools.RoutingTools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
// Add services to the container.
builder.Services.AddRazorPages();

// Every API controller requires an authenticated caller by default (there was
// previously no [Authorize] anywhere and app.UseAuthentication() was never
// called, so the whole JSON API was reachable anonymously regardless of the
// Razor-Page-level admin/staff/technician policies).
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AuthorizeFilter());
});

#region SignalR configuration
builder.Services.AddSignalR();
#endregion

#region Google geocoding service configuration
builder.Services.AddScoped<IGoogleGeocodingService, GoogleGeocodingService>();
#endregion

#region Connection to the database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
#endregion

#region Identity configuration
builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();
#endregion

#region MediatR configuration
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(IQueryService).Assembly, Assembly.GetExecutingAssembly()));
#endregion

#region AutoMapper configuration
var mapperConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new MappingProfile());
});

IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);
#endregion

#region MQTT configuration
builder.Services.AddSingleton<MqttMessageHandler>();
builder.Services.AddHostedService<MqttHostedService>();
#endregion

#region Google Authentication
builder.Services.AddAuthentication().AddGoogle(googleOptions =>
{
    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
});
#endregion

#region Authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.IsAdmin, p => p.AddRequirements(new IsAdmin()))
    .AddPolicy(PolicyNames.IsStaff, p => p.AddRequirements(new IsStaff()))
    .AddPolicy(PolicyNames.IsTechnician, p=> p.AddRequirements(new IsTechnician()));

builder.Services.AddScoped<IAuthorizationHandler, IsAdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, IsStaffAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, IsTechnicianAuthorizationHandler>();
#endregion

#region HttpClient configuration
builder.Services.AddTransient<CookieForwardingHandler>();
builder.Services.AddHttpClient("CityApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7027/");
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
})
.AddHttpMessageHandler<CookieForwardingHandler>();
#endregion

#region Ollama configuration
builder.Services.AddScoped<IOllamaService, OllamaService>();
# endregion


builder.Services.AddScoped<IUserContextService, UserContextService>();


builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IKnowledgeBaseRetriever, KnowledgeBaseRetriever>();

builder.Services.AddScoped<IRoutingTool, RoutingTool>();
builder.Services.AddScoped<IVehicleTool, VehicleTool>();

builder.Services.AddScoped<IIntentClassificationService, IntentClassificationService>();
builder.Services.AddScoped<IIntentRouterService, IntentRouterService>();
builder.Services.AddScoped<IToolExecutionService, ToolExecutionService>();


#region PayPal configuration
builder.Services.AddSingleton<IPayPalClient, PayPalClient>();
builder.Services.Configure<PayPalClientOptions>(options => 
{ 
    options.ClientId = builder.Configuration.GetRequiredSection("Payments:PayPal")["ClientId"]!;
    options.ClientSecret = builder.Configuration.GetRequiredSection("Payments:PayPal")["ClientSecret"]!;
    options.PayPalUrl = builder.Configuration.GetRequiredSection("Payments:PayPal")["PayPalUrl"]!;
});
#endregion

builder.Services.AddSingleton<TimerService>();

builder.Services.AddEndpointsApiExplorer();  // Necessario per .NET 7+
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Mobishare API",
        Version = "v1",
        Description = "API documentation for Mobishare"
    });

    c.EnableAnnotations();
    
    // Aggiungi i commenti XML (opzionale ma consigliato)
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mobishare API v1");
});


app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapControllers();

app.MapHub<ChatHub>("/chatHub");

app.MapHub<VehicleHub>("/vehicleHub");

app.MapHub<TimerHub>("/timerHub");

app.Run();
