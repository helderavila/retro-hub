using System.Net;
using RetroHub.Core.Bios;
using RetroHub.Core.Covers;
using RetroHub.Core.Input;
using RetroHub.Core.Install;
using RetroHub.Core.Launch;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Metadata;
using RetroHub.Core.Saves;
using RetroHub.Core.Scanning;

namespace RetroHub.Core;

/// <summary>Service composition (no DI container; the graph is small).</summary>
public sealed class HubServices : IDisposable
{
    public required AppPaths Paths { get; init; }
    public required ManifestRepository Manifests { get; init; }
    public required LibraryDatabase Database { get; init; }
    public required HttpClient Http { get; init; }
    public required DependencyInstaller Dependencies { get; init; }
    public required EmulatorInstaller Installer { get; init; }
    public required RomScanner Scanner { get; init; }
    public required BiosVerifier Bios { get; init; }
    public required GameLauncher Launcher { get; init; }
    public required CoverFetcher Covers { get; init; }
    public required ControllerConfigurator Controllers { get; init; }
    public required MetadataService Metadata { get; init; }
    public required SaveStateService Saves { get; init; }
    public required BackupService Backup { get; init; }
    public required CustomEmulators CustomEmulators { get; init; }

    /// <param name="gamepads">Connected controllers for the controller mapping; null = the platform default (XInput on Windows).</param>
    public static HubServices Create(string appBaseDirectory, Func<IReadOnlyList<IGamepadDevice>>? gamepads = null)
    {
        var paths = AppPaths.Create(appBaseDirectory);
        var manifests = ManifestRepository.Load(paths.Manifests, paths.UserManifests);
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();

        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RetroHub/0.1");

        var dependencies = new DependencyInstaller();
        var controllers = gamepads is null ? new ControllerConfigurator(db) : new ControllerConfigurator(db) { Devices = gamepads };
        return new HubServices
        {
            Paths = paths,
            Manifests = manifests,
            Database = db,
            Http = http,
            Dependencies = dependencies,
            Installer = new EmulatorInstaller(paths, http, new DownloadSourceResolver(http), dependencies, manifests, db),
            Scanner = new RomScanner(manifests),
            Bios = new BiosVerifier(manifests, paths, db),
            Launcher = new GameLauncher(manifests, db, controllers),
            Controllers = controllers,
            Covers = new CoverFetcher(http, paths, manifests, db),
            Metadata = new MetadataService(http, paths, manifests, db),
            Saves = new SaveStateService(manifests, db),
            Backup = new BackupService(paths, manifests, db),
            CustomEmulators = new CustomEmulators(paths, manifests, db),
        };
    }

    public void Dispose() => Http.Dispose();
}
