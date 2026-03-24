using System.Threading.Channels;  /////
using ContentAgent.Api.HostedServices;
using ContentAgent.Api.Services;
using ContentAgent.Video;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// log4net: same Azure file path as dance.api (D:\home\site\log\); see log4net.config. Unrelated to agents/**/log.md.
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddLog4Net(Path.Combine(AppContext.BaseDirectory, "log4net.config"));

builder.Services.AddSingleton(Channel.CreateUnbounded<bool>());
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<IAgentPipelineService, AgentPipelineService>();
builder.Services.AddHttpClient<ISitemapSubmissionService, SitemapSubmissionService>();
builder.Services.AddHttpClient<IGitHubMergeService, GitHubMergeService>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
});
builder.Services.Configure<BufferOptions>(builder.Configuration.GetSection(BufferOptions.SectionName));
builder.Services.Configure<VideoAssetPathOptions>(builder.Configuration.GetSection(VideoAssetPathOptions.SectionName));
builder.Services.AddHttpClient<IBufferScheduleService, BufferScheduleService>();
builder.Services.AddScoped<IStagingPromotionService, StagingPromotionService>();
builder.Services.AddSingleton<ISlideHelloWorldVideoService>(sp =>
    new VideoService(
        AppContext.BaseDirectory,
        sp.GetRequiredService<ILogger<VideoService>>(),
        sp.GetRequiredService<IOptions<VideoAssetPathOptions>>().Value));
builder.Services.AddHostedService<AgentBackgroundService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Content Agent API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// Serve generated quiz videos and other static files from wwwroot (e.g. /videos/22.mp4).
app.UseStaticFiles();

app.MapGet("/", () => Results.Json(new
{
    name = "Content Agent API",
    swagger = "/swagger",
    runAgent = "POST /api/agent/run",
    submitSitemaps = "POST /api/agent/submit-sitemaps",
    promoteStaging = "POST /api/agent/promote",
    createQuizVideo = "POST /api/video",
    publicQuizVideos = "/videos/{day}.mp4 (after generation)"
}));

app.MapControllers();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Content Agent API starting up (Environment: {Environment})", app.Environment.EnvironmentName);

app.Run();
