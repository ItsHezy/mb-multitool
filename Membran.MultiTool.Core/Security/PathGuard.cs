namespace Membran.MultiTool.Core.Security;

public sealed class PathGuard
{
    private readonly string[] _allowedRoots;

    public PathGuard()
    {
        _allowedRoots = BuildAllowedRoots();
    }

    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    public bool IsAllowed(string path)
    {
        var normalized = ExpandAndNormalize(path);
        return _allowedRoots.Any(root =>
            normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    public static string ExpandAndNormalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(expanded);
    }

    private static string[] BuildAllowedRoots()
    {
        static string FullOrEmpty(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
        }
        .Select(FullOrEmpty)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return roots;
    }
}
