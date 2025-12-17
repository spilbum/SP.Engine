using ResourceServer;
using ResourceServer.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 256 * 1024);
builder.Logging.AddConsole();
builder.Services.AddDatabaseHandler(builder.Configuration);
builder.Services.AddResourceServices();
builder.Services.AddJsonGateway();

var app = builder.Build();
app.MapHealthApi();
app.MapJsonGateway();
app.Run();

