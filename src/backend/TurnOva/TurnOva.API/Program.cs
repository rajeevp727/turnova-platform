using TurnOva.Application.Dispatch;
using TurnOva.Infrastructure.Dispatch;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IQueueStore, InMemoryQueueStore>();
builder.Services.AddScoped<QueueService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TurnOva.API",
    status = "healthy",
    environment = app.Environment.EnvironmentName
}));

app.MapGet("/api/v1/zones/{zoneId}/queues/{serviceClass}", async (
    string zoneId, string serviceClass, QueueService queueService, CancellationToken ct) =>
{
    var entries = await queueService.GetPositionedQueueAsync(zoneId, serviceClass, ct);
    return Results.Ok(entries.Select((entry, index) => new
    {
        entry.DriverId,
        entry.ZoneId,
        entry.ServiceClass,
        position = index + 1,
        entry.JoinedAt
    }));
});

app.Run();

public partial class Program;