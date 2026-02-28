using System.Text.Json;
using Membran.MultiTool.Core.Models;

namespace Membran.MultiTool.Core.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] KnownKeys =
    [
        "dry_run_default",
        "auto_yes_default",
        "restore_point_default",
        "output_format",
        "osint_timeout_sec",
        "osint_run_all_found_default",
        "osint_max_parallel_tools",
        "osint_output_include_raw"
    ];

    private readonly string _configPath;

    public ConfigStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _configPath = Path.Combine(rootDirectory, "config.json");
    }

    public MbConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return new MbConfig();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MbConfig>(File.ReadAllText(_configPath), JsonOptions);
            if (parsed is null)
            {
                return new MbConfig();
            }

            var normalizedFormat = NormalizeOutputFormat(parsed.OutputFormat);
            var timeout = parsed.OsintTimeoutSec is > 0 and <= 3600 ? parsed.OsintTimeoutSec : 120;
            var maxParallel = parsed.OsintMaxParallelTools is > 0 and <= 16 ? parsed.OsintMaxParallelTools : 3;

            return parsed with
            {
                OutputFormat = normalizedFormat,
                OsintTimeoutSec = timeout,
                OsintMaxParallelTools = maxParallel
            };
        }
        catch
        {
            return new MbConfig();
        }
    }

    public void Save(MbConfig config)
    {
        var normalized = config with
        {
            OutputFormat = NormalizeOutputFormat(config.OutputFormat),
            OsintTimeoutSec = config.OsintTimeoutSec is > 0 and <= 3600 ? config.OsintTimeoutSec : 120,
            OsintMaxParallelTools = config.OsintMaxParallelTools is > 0 and <= 16 ? config.OsintMaxParallelTools : 3
        };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public IReadOnlyList<string> ListKeys()
    {
        return KnownKeys;
    }

    public bool TryGetValue(MbConfig config, string key, out string value)
    {
        value = string.Empty;
        return NormalizeKey(key) switch
        {
            "dry_run_default" => TrySetOut(config.DryRunDefault.ToString().ToLowerInvariant(), out value),
            "auto_yes_default" => TrySetOut(config.AutoYesDefault.ToString().ToLowerInvariant(), out value),
            "restore_point_default" => TrySetOut(config.RestorePointDefault.ToString().ToLowerInvariant(), out value),
            "output_format" => TrySetOut(config.OutputFormat, out value),
            "osint_timeout_sec" => TrySetOut(config.OsintTimeoutSec.ToString(), out value),
            "osint_run_all_found_default" => TrySetOut(config.OsintRunAllFoundDefault.ToString().ToLowerInvariant(), out value),
            "osint_max_parallel_tools" => TrySetOut(config.OsintMaxParallelTools.ToString(), out value),
            "osint_output_include_raw" => TrySetOut(config.OsintOutputIncludeRaw.ToString().ToLowerInvariant(), out value),
            _ => false
        };
    }

    public bool TrySet(MbConfig current, string key, string rawValue, out MbConfig updated, out string error)
    {
        updated = current;
        error = string.Empty;

        var normalizedKey = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            error = "Key is required.";
            return false;
        }

        switch (normalizedKey)
        {
            case "dry_run_default":
            {
                if (!bool.TryParse(rawValue, out var parsed))
                {
                    error = "dry_run_default must be true or false.";
                    return false;
                }

                updated = current with { DryRunDefault = parsed };
                return true;
            }
            case "auto_yes_default":
            {
                if (!bool.TryParse(rawValue, out var parsed))
                {
                    error = "auto_yes_default must be true or false.";
                    return false;
                }

                updated = current with { AutoYesDefault = parsed };
                return true;
            }
            case "restore_point_default":
            {
                if (!bool.TryParse(rawValue, out var parsed))
                {
                    error = "restore_point_default must be true or false.";
                    return false;
                }

                updated = current with { RestorePointDefault = parsed };
                return true;
            }
            case "output_format":
            {
                var normalized = NormalizeOutputFormat(rawValue);
                if (normalized is not ("text" or "json"))
                {
                    error = "output_format must be text or json.";
                    return false;
                }

                updated = current with { OutputFormat = normalized };
                return true;
            }
            case "osint_timeout_sec":
            {
                if (!int.TryParse(rawValue, out var parsed) || parsed < 1 || parsed > 3600)
                {
                    error = "osint_timeout_sec must be an integer between 1 and 3600.";
                    return false;
                }

                updated = current with { OsintTimeoutSec = parsed };
                return true;
            }
            case "osint_run_all_found_default":
            {
                if (!bool.TryParse(rawValue, out var parsed))
                {
                    error = "osint_run_all_found_default must be true or false.";
                    return false;
                }

                updated = current with { OsintRunAllFoundDefault = parsed };
                return true;
            }
            case "osint_max_parallel_tools":
            {
                if (!int.TryParse(rawValue, out var parsed) || parsed < 1 || parsed > 16)
                {
                    error = "osint_max_parallel_tools must be an integer between 1 and 16.";
                    return false;
                }

                updated = current with { OsintMaxParallelTools = parsed };
                return true;
            }
            case "osint_output_include_raw":
            {
                if (!bool.TryParse(rawValue, out var parsed))
                {
                    error = "osint_output_include_raw must be true or false.";
                    return false;
                }

                updated = current with { OsintOutputIncludeRaw = parsed };
                return true;
            }
            default:
                error = $"Unknown key '{key}'. Known keys: {string.Join(", ", KnownKeys)}";
                return false;
        }
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().ToLowerInvariant().Replace('-', '_');
    }

    private static string NormalizeOutputFormat(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "json" ? "json" : "text";
    }

    private static bool TrySetOut(string source, out string value)
    {
        value = source;
        return true;
    }
}
