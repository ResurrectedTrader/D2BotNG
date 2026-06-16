using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using D2BotNG.Data;
using D2BotNG.Engine;
using D2BotNG.Engine.Handoff;
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
using static D2BotNG.Windows.NativeMethods;
using static D2BotNG.Windows.NativeTypes;
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
        var handoffContext = BuildHandoffContext(args);
        UpdateManager.CleanupOldExeAfterUpdate(handoffContext.Manifest?.OldPid);

        // Configure logging with async sinks to avoid thread pool starvation
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
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

            builder.Services.AddSingleton(handoffContext);
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

            // In takeover mode the predecessor still holds the server port until its
            // host finishes shutting down. Wait for the port to become bindable before
            // Kestrel tries to claim it.
            if (handoffContext.IsTakeover)
            {
                WaitForPortBindable(settings.Server.Host, (int)settings.Server.Port, TimeSpan.FromSeconds(30));
            }

            // Create the message-only window for WM_COPYDATA IPC BEFORE the host starts.
            // EngineHostedService.StartAsync (and any handoff RehydrateAsync inside it) reads
            // MessageWindow.Handle, so it must be valid by then. In GUI mode, MainForm no
            // longer switches the handle when it loads — the message-only window owns it
            // for the full process lifetime.
            var messageWindow = app.Services.GetRequiredService<MessageWindow>();
            messageWindow.CreateMessageOnlyWindow();

            if (headless)
            {
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
                // AddressAlreadyInUse (10048) = another non-exclusive binder.
                // AccessDenied        (10013) = port held with SO_EXCLUSIVEADDRUSE,
                //                               or excluded by Windows reserved ranges.
                // Both surface to the user as "this port isn't available".
                var isPortConflict = innerEx is SocketException se &&
                    (se.SocketErrorCode == SocketError.AddressAlreadyInUse ||
                     se.SocketErrorCode == SocketError.AccessDenied);
                if (isPortConflict)
                {
                    var settingsPath = Path.Combine(AppContext.BaseDirectory, "d2botng.json");
                    throw new Exception(
                        $"Could not start server on {serverUrl} because the port is already in use.\n\n" +
                        "Another instance of D2BotNG may already be running, or another application is using this port.\n\n" +
                        "To fix this, either close the other application, or change the port D2BotNG uses " +
                        $"by opening this file in a text editor:\n{settingsPath}\n" +
                        "and changing the \"server.port\" value to a different number (e.g., 5001).\n\n" +
                        "Then launch D2BotNG again.",
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
        var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var form = new MainForm(webViewUrl, settingsRepo, profileEngine, appLifetime);

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
        var deadline = DateTime.UtcNow + timeout;

        // Phase 1: wait for our own host to finish starting. Without this, an HTTP
        // probe could succeed against ANOTHER process bound to the same port —
        // which would make a second instance silently believe its bind succeeded.
        while (DateTime.UtcNow < deadline)
        {
            if (serverTask.IsFaulted)
            {
                return false;
            }
            if (serverTask.IsCompletedSuccessfully)
            {
                break;
            }
            Thread.Sleep(50);
        }
        if (!serverTask.IsCompletedSuccessfully)
        {
            return false;
        }

        // Phase 2: confirm the HTTP endpoint is actually responding.
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
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
                // Server not accepting yet, keep trying
            }
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>
    /// Parses <c>--takeover &lt;manifest-path&gt;</c> from the CLI args. If present, reads the
    /// manifest, duplicates the predecessor's job handle into this process, and returns a
    /// populated <see cref="HandoffContext"/>. Otherwise returns an empty context.
    /// </summary>
    private static HandoffContext BuildHandoffContext(string[] args)
    {
        var idx = Array.IndexOf(args, HandoffManager.TakeoverFlag);
        if (idx < 0 || idx + 1 >= args.Length)
            return new HandoffContext();

        var manifestPath = args[idx + 1];
        var json = File.ReadAllText(manifestPath);
        var manifest = HandoffManager.DeserializeManifest(json);

        if (manifest.SchemaVersion != HandoffManifest.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Handoff manifest schema version {manifest.SchemaVersion} does not match current {HandoffManifest.CurrentSchemaVersion}");
        }

        var oldProcess = OpenProcess(PROCESS_DUP_HANDLE, false, manifest.OldPid);
        if (oldProcess == 0)
        {
            throw new InvalidOperationException(
                $"Cannot open predecessor PID {manifest.OldPid} for handle duplication (last error {Marshal.GetLastWin32Error()})");
        }

        try
        {
            if (!DuplicateHandle(
                oldProcess,
                (nint)manifest.JobHandle,
                GetCurrentProcess(),
                out var adoptedJobHandle,
                0,
                false,
                DUPLICATE_SAME_ACCESS))
            {
                throw new InvalidOperationException(
                    $"DuplicateHandle failed for job handle (last error {Marshal.GetLastWin32Error()})");
            }

            // Tell the predecessor we own the job now so it can quiesce its monitor loops
            // before we re-register the manager handle with running games.
            SignalNamedEvent(manifest.AdoptedEventName);

            return new HandoffContext
            {
                IsTakeover = true,
                AdoptedJobHandle = adoptedJobHandle,
                Manifest = manifest,
                ManifestPath = manifestPath
            };
        }
        finally
        {
            CloseHandle(oldProcess);
        }
    }

    private static void SignalNamedEvent(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!EventWaitHandle.TryOpenExisting(name, out var evt)) return;
        using (evt) evt.Set();
    }

    /// <summary>
    /// Polls until a TCP listener can bind the given host:port (or throws on timeout).
    /// Used by a successor process to wait for the predecessor to release the server port.
    /// </summary>
    private static void WaitForPortBindable(string host, int port, TimeSpan timeout)
    {
        // Fall back to IPAddress.Any for hostnames like "localhost" or "0.0.0.0".
        var address = IPAddress.TryParse(host, out var parsed) ? parsed : IPAddress.Any;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var listener = new TcpListener(address, port);
                listener.Start();
                listener.Stop();
                Logger.Information("Predecessor released {Host}:{Port}", host, port);
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
            }
        }
        throw new TimeoutException($"Predecessor did not release {host}:{port} within {timeout.TotalSeconds}s");
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
        services.AddSingletonWithHandoff<KeyListRepository>();
        services.AddSingleton<ProxyRepository>();
        services.AddSingleton<ScheduleRepository>();
        services.AddSingleton<ItemRepository>();
        services.AddSingleton<PatchRepository>();
        services.AddSingleton<CharacterRepository>();

        // Add Windows integration services
        services.AddSingleton<DaclOverwriter>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<Patcher>();
        services.AddSingleton<ProcessManager>();
        services.AddSingleton<MessageWindow>();

        // Add data cache for D2BS store/retrieve/delete
        services.AddSingletonWithHandoff<DataCache>();

        // Add d2bs.ini writer
        services.AddSingleton<IniWriter>();

        // Add rendering services
        services.AddSingleton<PaletteManager>();
        services.AddSingleton<ItemRenderer>();

        // Add message service (centralized console messages)
        services.AddSingletonWithHandoff<MessageService>();

        // Add Discord webhook service (per-profile webhooks for items/console/announce)
        services.AddSingleton<DiscordWebhookService>();

        // Add logger registry (per-logger level filtering for UI console)
        services.AddSingletonWithHandoff<LoggerRegistry>();

        // Add engines
        services.AddSingleton<ProfileEngine>();
        services.AddSingleton<ScheduleEngine>();

        // Add update manager
        services.AddSingleton<UpdateManager>();

        // Add handoff manager (orchestrates in-place process restart)
        services.AddSingleton<HandoffManager>();

        // Add legacy API services
        services.AddHttpClient();
        services.AddSingletonWithHandoff<SessionManager>();
        services.AddSingletonWithHandoff<NotificationQueue>();
        services.AddSingletonWithHandoff<WebhookService>();
        services.AddSingletonWithHandoff<GameActionScheduler>();
        services.AddScoped<LegacyApiHandler>();

        // DiscordService is a participant; register as singleton + hosted service so the
        // same instance can be resolved by the HandoffManager
        services.AddSingletonWithHandoff<DiscordService>();

        // Character state service (live character snapshots from the bot engine)
        services.AddSingleton<CharacterStateService>();

        // Add hosted services
        services.AddHostedService<EngineHostedService>();
        services.AddHostedService<ErrorDialogWatcher>();
        services.AddHostedService<UpdateCheckBackgroundService>();
        services.AddHostedService<GameDirectoryCleanupService>();
        services.AddHostedService<D2BSMessageHandler>();
        services.AddHostedService(sp => sp.GetRequiredService<CharacterStateService>());
        services.AddHostedService(sp => sp.GetRequiredService<DiscordService>());
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
        app.MapGrpcService<ProxyServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<ScheduleServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<SettingsServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<EventServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<FileServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<UpdateServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<ItemServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<LoggingServiceImpl>().EnableGrpcWeb();
        app.MapGrpcService<CharacterServiceImpl>().EnableGrpcWeb();

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
