using Autopelago.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<AutopelagoGameService>();

WebApplication app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
