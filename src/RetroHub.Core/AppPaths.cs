namespace RetroHub.Core;

/// <summary>
/// Layout de pastas. Se existir um "portable.txt" ao lado do executável, tudo fica em ./data;
/// caso contrário, em %LOCALAPPDATA%\RetroHub.
/// </summary>
public sealed class AppPaths
{
    public required string Root { get; init; }
    public required string Manifests { get; init; }
    public bool IsPortable { get; init; }

    public string Emulators => Path.Combine(Root, "emulators");
    public string Downloads => Path.Combine(Root, "downloads");
    public string Staging => Path.Combine(Root, "downloads", "staging");
    public string Bios => Path.Combine(Root, "bios");
    public string Covers => Path.Combine(Root, "covers");
    public string Database => Path.Combine(Root, "library.db");

    /// <summary>
    /// Manifestos do usuário: emulators\*.json e systems\*.json somados aos que vêm com o app
    /// (com o mesmo id, o do usuário vale). Ficam fora da pasta do programa: atualizações não apagam.
    /// </summary>
    public string UserManifests => Path.Combine(Root, "manifests");

    public static AppPaths Create(string appBaseDir)
    {
        var portable = File.Exists(Path.Combine(appBaseDir, "portable.txt"));
        var root = portable
            ? Path.Combine(appBaseDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroHub");

        var paths = new AppPaths
        {
            Root = root,
            Manifests = Path.Combine(appBaseDir, "manifests"),
            IsPortable = portable,
        };
        paths.EnsureCreated();
        return paths;
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Emulators, Downloads, Staging, Bios, Covers,
                     Path.Combine(UserManifests, "emulators"), Path.Combine(UserManifests, "systems") })
            Directory.CreateDirectory(dir);
    }

    public string EmulatorDir(string emulatorId) => Path.Combine(Emulators, emulatorId);
}
