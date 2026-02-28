using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Legacy.Api;
using D2BotNG.Legacy.Models;
using D2BotNG.Logging;
using D2BotNG.Rendering;
using D2BotNG.Services;
using D2BotNG.UI;
using D2BotNG.Windows;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

// For SerilogLoggerFactory

namespace D2BotNG;

internal static class Program
{
    private static readonly ILogger Logger = TrackingLoggerFactory.ForContext(typeof(Program));

    [STAThread]
    private static void Main(string[] args)
    {
        var headless = args.Contains("--headless");
        var devUi = args.Contains("--dev-ui");

        // Configure logging with async sinks to avoid thread pool starvation
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .Filter.With<LoggerRegistry>()
            .WriteTo.Async(a => a.Console())
            .WriteTo.Async(a => a.File("logs/d2bot-.log", rollingInterval: RollingInterval.Day))
            .WriteTo.MessageService()
            .CreateLogger();

        try
        {
            // Build and configure the web application
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            // Wrap ILoggerFactory to track all logger categories (last registration wins)
            builder.Services.AddSingleton<ILoggerFactory>(_ => new TrackingLoggerFactory(new SerilogLoggerFactory()));

            // Reduce shutdown timeout so Kestrel doesn't wait 30s draining connections
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.Zero);

            ConfigureServices(builder.Services);

            var app = builder.Build();

            // Initialize the MessageService sink for logging to console panel (do this early)
            var messageService = app.Services.GetRequiredService<MessageService>();
            var loggerRegistry = app.Services.GetRequiredService<LoggerRegistry>();
            MessageServiceSink.Initialize(messageService);
            TrackingLoggerFactory.Initialize(loggerRegistry);

            // Migrate legacy data files using the configured base path from settings
            var settingsRepository = app.Services.GetRequiredService<SettingsRepository>();
            var settings = settingsRepository.GetAsync().GetAwaiter().GetResult();
            var basePath = string.IsNullOrWhiteSpace(settings.BasePath)
                ? AppContext.BaseDirectory
                : settings.BasePath;
            Migration.MigrateIfNeeded(basePath);

            Logger.Information("D2BotNG starting in {Mode} mode on port {Port}...", headless ? "headless" : "GUI", settings.Server.Port);

            ConfigureApp(app, devUi);

            // Initialize item repository (loads entities into memory, starts file watcher)
            var itemRepository = app.Services.GetRequiredService<ItemRepository>();
            itemRepository.InitializeAsync().GetAwaiter().GetResult();

            // Get server URL from settings
            var serverUrl = $"http://{settings.Server.Host}:{settings.Server.Port}";

            if (headless)
            {
                // Headless mode: create message-only window for D2BS IPC
                var messageWindow = app.Services.GetRequiredService<MessageWindow>();
                messageWindow.CreateMessageOnlyWindow();

                Logger.Information("Server will run on {Url}", serverUrl);

                // Run the server
                app.Run(serverUrl);
            }
            else
            {
                // GUI mode: run server in background and show WinForms UI
                RunWithGui(app, serverUrl, settingsRepository);
            }

            Logger.Information("D2BotNG shutting down...");
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "D2BotNG crashed");
            MessageBox.Show(
                $"D2BotNG encountered a fatal error:\n\n{ex.Message}",
                "Fatal Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RunWithGui(WebApplication app, string serverUrl, SettingsRepository settingsRepo)
    {
        // Start server directly (not RunAsync which adds WaitForShutdownAsync overhead
        // and makes clean shutdown difficult — we manage the lifecycle ourselves).
        app.Urls.Add(serverUrl);
        var startTask = app.StartAsync();

        // Wait for server to be ready (use localhost for health check since 0.0.0.0 won't respond to client requests)
        var healthCheckUrl = serverUrl.Replace("0.0.0.0", "127.0.0.1");
        if (!WaitForServerReady(healthCheckUrl, startTask, TimeSpan.FromSeconds(30)))
        {
            if (startTask.IsFaulted)
            {
                var innerEx = startTask.Exception!.GetBaseException();
                if (innerEx.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"Could not start server on {serverUrl} because the port is already in use.\n\n" +
                        "Another instance of D2BotNG may already be running, or another application is using this port.\n\n" +
                        "Close the other application or change the port in d2botng.json.",
                        innerEx);
                }
                throw startTask.Exception.GetBaseException();
            }
            throw new Exception("Server failed to start within timeout");
        }

        // Check if we should start minimized
        var settings = settingsRepo.GetAsync().GetAwaiter().GetResult();
        var startMinimized = settings.StartMinimized;

        // Start Windows Forms application
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Use localhost for WebView since it can't connect to 0.0.0.0
        var webViewUrl = serverUrl.Replace("0.0.0.0", "127.0.0.1");
        var profileEngine = app.Services.GetRequiredService<ProfileEngine>();
        var messageWindow = app.Services.GetRequiredService<MessageWindow>();
        var form = new MainForm(webViewUrl, settingsRepo, profileEngine, messageWindow);

        if (startMinimized)
        {
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
        }

        Application.Run(form);

        // Form closed — stop the host directly. Since we used StartAsync (not RunAsync),
        // there's no WaitForShutdownAsync in play, so this is the single StopAsync call.
        app.StopAsync().GetAwaiter().GetResult();
    }

    private static bool WaitForServerReady(string url, Task serverTask, TimeSpan timeout)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            // If the server task has faulted, stop waiting immediately
            if (serverTask.IsFaulted)
            {
                return false;
            }

            try
            {
                var response = client.GetAsync(url).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Server not ready yet, keep trying
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add gRPC services with auth interceptor
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<AuthInterceptor>();
        });
        services.AddControllers();

        // Add event broadcaster for real-time updates (before repositories that depend on it)
        services.AddSingleton<EventBroadcaster>();

        // Add data repositories
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<Paths>();
        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<KeyListRepository>();
        services.AddSingleton<ScheduleRepository>();
        services.AddSingleton<ItemRepository>();
        services.AddSingleton<PatchRepository>();

        // Add Windows integration services
        services.AddSingleton<DaclOverwriter>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<Patcher>();
        services.AddSingleton<ProcessManager>();
        services.AddSingleton<MessageWindow>();

        // Add data cache for D2BS store/retrieve/delete
        services.AddSingleton<DataCache>();

        // Add d2bs.ini writer
        services.AddSingleton<IniWriter>();

        // Add rendering services
        services.AddSingleton<PaletteManager>();
        services.AddSingleton<ItemRenderer>();

        // Add message service (centralized console messages)
        services.AddSingleton<MessageService>();

        // Add logger registry (per-logger level filtering for UI console)
        services.AddSingleton<LoggerRegistry>();

        // Add engines
        services.AddSingleton<ProfileEngine>();
        services.AddSingleton<ScheduleEngine>();

        // Add update manager
        services.AddSingleton<UpdateManager>();

        // Add legacy API services
        services.AddHttpClient();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<NotificationQueue>();
        services.AddSingleton<WebhookService>();
        services.AddSingleton<GameActionScheduler>();
        services.AddScoped<LegacyApiHandler>();

        // Add hosted services
        services.AddHostedService<EngineHostedService>();
        services.AddHostedService<ErrorDialogWatcher>();
        services.AddHostedService<UpdateCheckBackgroundService>();
        services.AddHostedService<D2BSMessageHandler>();
        services.AddHostedService<DiscordService>();
        services.AddHostedService(sp => sp.GetRequiredService<GameActionScheduler>());

        // CORS for development
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
            });
        });
    }

    private static void ConfigureApp(WebApplication app, bool devUi = false)
    {
        // Use CORS
        app.UseCors();

        // Legacy D2Bot# API compatibility middleware (before gRPC-Web
        // so it sees the raw request body before the gRPC-Web stream wrapping)
        app.UseMiddleware<LegacyApiMiddleware>();

        // Enable gRPC-Web
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

        // Map controllers
        app.MapControllers();

        // Map gRPC services
        app.MapGrpcService<ProfileServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<KeyServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<ScheduleServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<SettingsServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<EventServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<FileServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<UpdateServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<ItemServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<LoggingServiceImpl>().EnableGrpcWeb();

        // Serve static files - embedded resources by default, file system with --dev-ui flag
        var embeddedProvider = new EmbeddedResourceFileProvider(typeof(Program).Assembly);
        IFileProvider fileProvider;

        if (devUi)
        {
            // Development mode: UI from file system, rendering assets from embedded
            var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            Logger.Information("Serving UI from file system: {Path}", wwwrootPath);
            Logger.Information("Serving rendering assets from embedded resources");
            var physicalProvider = new PhysicalFileProvider(wwwrootPath);
            fileProvider = new CompositeFileProvider(physicalProvider, embeddedProvider);
        }
        else
        {
            // Production mode: serve from embedded resources
            Logger.Information("Serving UI from embedded resources");
            fileProvider = embeddedProvider;
        }

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });

        // Configure content types for game asset files
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".dc6"] = "application/octet-stream";
        contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
        contentTypeProvider.Mappings[".PL2"] = "application/octet-stream";
        contentTypeProvider.Mappings[".pl2"] = "application/octet-stream";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = contentTypeProvider
        });

        // SPA fallback - serve index.html for client-side routing
        app.MapFallback(async context =>
        {
            var indexFile = fileProvider.GetFileInfo("index.html");
            if (indexFile.Exists)
            {
                context.Response.ContentType = "text/html";
                await using var stream = indexFile.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
    }
}
