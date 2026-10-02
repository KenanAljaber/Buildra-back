using Buildra.Worker;
using Buildra.Application.Models;
using Buildra.Application.Planning;
using Buildra.Infrastructure.Models;
using Buildra.Infrastructure.Persistence;
using Buildra.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<BuildraDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Buildra")));
builder.Services.AddScoped<IPlanningStore, EfPlanningStore>();
builder.Services.AddScoped<ExecutePlanningJob>();
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.PostConfigure<OpenAIOptions>(o => { if (string.IsNullOrWhiteSpace(o.ApiKey)) o.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? ""; });
builder.Services.AddHttpClient<IModelProvider, OpenAIModelProvider>(http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();
