using Autopelago.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CurrentGameStates>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHostedService<AutopelagoGameService>();

WebApplication app = builder.Build();

app.MapGet("/currentState/{slotName}", (string slotName, CurrentGameStates gameStates) => gameStates.Get(slotName)?.ToProxy());

app.Run();
