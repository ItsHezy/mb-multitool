using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Membran.MultiTool.Cli;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Services;
using Membran.MultiTool.Core.Windows;
using Membran.MultiTool.Geo.Providers;
using Membran.MultiTool.Geo.Services;
using Membran.MultiTool.Uninstall.Services;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};
jsonOptions.Converters.Add(new JsonStringEnumConverter());

var persistenceStore = new PersistenceStore();
var configStore = new ConfigStore(persistenceStore.RootDirectory);
var pathGuard = new PathGuard();
var commandRunner = new SystemCommandRunner();
var appDiscoveryService = new AppDiscoveryService();
var uninstallPlanner = new UninstallPlanner(pathGuard, commandRunner);
var uninstallExecutor = new UninstallExecutor(pathGuard, commandRunner);

using var httpClient = new HttpClient();
var geoLookupService = new GeoLookupService(
[
    new IpApiGeoProvider(httpClient),
    new IpInfoGeoProvider(httpClient)
]);

args = CliRuntime.NormalizeShortcutArgs(args);

var root = new RootCommand("Membran MultiTool v1");

var geoCommand = new Command("geo", "Geolocation operations.");
var geoLookupCommand = new Command("lookup", "Lookup location metadata for self/public IP or provided IP.");
var geoIpOption = new Option<string>("--ip", () => "self", "self, IPv4, or IPv6.");
var geoJsonOption = new Option<bool>("--json", "Print JSON output.");
geoLookupCommand.AddOption(geoIpOption);
geoLookupCommand.AddOption(geoJsonOption);
geoLookupCommand.SetHandler(
    async (ip, asJson) =>
    {
        var exitCode = await CliRuntime.HandleGeoLookupAsync(geoLookupService, ip, asJson).ConfigureAwait(false);
        Environment.ExitCode = exitCode;
    },
    geoIpOption,
    geoJsonOption);
geoCommand.AddCommand(geoLookupCommand);

var ipCommand = new Command("ip", "Quick geolocation command (aliases: -IP, --IP, /IP).");
var ipTargetArgument = new Argument<string>("target", () => "self", "self, IPv4, or IPv6.");
var ipJsonOption = new Option<bool>("--json", "Print JSON output.");
ipCommand.AddArgument(ipTargetArgument);
ipCommand.AddOption(ipJsonOption);
ipCommand.SetHandler(
    async (InvocationContext context) =>
    {
        var target = context.ParseResult.GetValueForArgument(ipTargetArgument) ?? "self";
        var currentConfig = configStore.Load();
        var jsonProvided = context.ParseResult.FindResultFor(ipJsonOption) is not null;
        var asJson = CliRuntime.ResolveJsonOutput(
            context.ParseResult.GetValueForOption(ipJsonOption),
            jsonProvided,
            currentConfig);

        var exitCode = await CliRuntime.HandleGeoLookupAsync(geoLookupService, target, asJson).ConfigureAwait(false);
        context.ExitCode = exitCode;
    });

var ipBatchCommand = new Command("batch", "Lookup IP addresses from a text file (one IP per line).");
var ipBatchFileOption = new Option<string>("--file", "Path to a file with one IP per line.") { IsRequired = true };
var ipBatchJsonOption = new Option<bool>("--json", "Print JSON output.");
ipBatchCommand.AddOption(ipBatchFileOption);
ipBatchCommand.AddOption(ipBatchJsonOption);
ipBatchCommand.SetHandler(
    async (InvocationContext context) =>
    {
        var file = context.ParseResult.GetValueForOption(ipBatchFileOption) ?? string.Empty;
        var currentConfig = configStore.Load();
        var jsonProvided = context.ParseResult.FindResultFor(ipBatchJsonOption) is not null;
        var asJson = CliRuntime.ResolveJsonOutput(
            context.ParseResult.GetValueForOption(ipBatchJsonOption),
            jsonProvided,
            currentConfig);

        var exitCode = await CliRuntime.HandleIpBatchAsync(geoLookupService, file, asJson, jsonOptions).ConfigureAwait(false);
        context.ExitCode = exitCode;
    });
ipCommand.AddCommand(ipBatchCommand);

var uiCommand = new Command("ui", "Quick uninstall-by-path command (aliases: -ui, --ui, /ui).");
var uiPathArgument = new Argument<string>("path", "File or folder path to clean.");
var uiYesOption = new Option<bool>(new[] { "--yes", "-y" }, "Skip interactive confirmation prompt.");
var uiDryRunOption = new Option<bool>("--dry-run", "Run execution without mutating system state.");
var uiJsonOption = new Option<bool>("--json", "Print JSON output.");
var uiNoRestorePointOption = new Option<bool>("--no-restore-point", "Disable restore-point attempt.");
var uiExcludeOption = new Option<string[]>("--exclude", "Exclude artifact patterns (supports * and ? wildcards).")
{
    AllowMultipleArgumentsPerToken = true
};
var uiElevateOption = new Option<bool>("--elevate", "When needed, relaunch this ui cleanup as Administrator.");
var uiElevatedInternalOption = new Option<bool>("--elevated-internal") { IsHidden = true };
uiCommand.AddArgument(uiPathArgument);
uiCommand.AddOption(uiYesOption);
uiCommand.AddOption(uiDryRunOption);
uiCommand.AddOption(uiJsonOption);
uiCommand.AddOption(uiNoRestorePointOption);
uiCommand.AddOption(uiExcludeOption);
uiCommand.AddOption(uiElevateOption);
uiCommand.AddOption(uiElevatedInternalOption);
uiCommand.SetHandler(
    async (InvocationContext context) =>
    {
        var currentConfig = configStore.Load();

        var path = context.ParseResult.GetValueForArgument(uiPathArgument) ?? string.Empty;

        var autoYesProvided = context.ParseResult.FindResultFor(uiYesOption) is not null;
        var autoYes = autoYesProvided
            ? context.ParseResult.GetValueForOption(uiYesOption)
            : currentConfig.AutoYesDefault;

        var dryRunProvided = context.ParseResult.FindResultFor(uiDryRunOption) is not null;
        var dryRun = dryRunProvided
            ? context.ParseResult.GetValueForOption(uiDryRunOption)
            : currentConfig.DryRunDefault;

        var noRestorePointProvided = context.ParseResult.FindResultFor(uiNoRestorePointOption) is not null;
        var attemptRestorePoint = noRestorePointProvided
            ? !context.ParseResult.GetValueForOption(uiNoRestorePointOption)
            : currentConfig.RestorePointDefault;

        var jsonProvided = context.ParseResult.FindResultFor(uiJsonOption) is not null;
        var asJson = CliRuntime.ResolveJsonOutput(
            context.ParseResult.GetValueForOption(uiJsonOption),
            jsonProvided,
            currentConfig);

        var excludePatterns = context.ParseResult.GetValueForOption(uiExcludeOption) ?? Array.Empty<string>();
        var elevate = context.ParseResult.GetValueForOption(uiElevateOption);
        var elevatedInternal = context.ParseResult.GetValueForOption(uiElevatedInternalOption);

        var exitCode = await CliRuntime.HandleUiCommandAsync(
                path,
                autoYes,
                dryRun,
                asJson,
                attemptRestorePoint,
                excludePatterns,
                elevate,
                elevatedInternal,
                appDiscoveryService,
                uninstallPlanner,
                uninstallExecutor,
                persistenceStore,
                jsonOptions)
            .ConfigureAwait(false);

        context.ExitCode = exitCode;
    });

var uninstallCommand = new Command("uninstall", "Uninstall and cleanup operations.");

var uninstallListCommand = new Command("list", "List discovered installed apps.");
var listJsonOption = new Option<bool>("--json", "Print JSON output.");
var includeSystemOption = new Option<bool>("--include-system", "Include system components.");
uninstallListCommand.AddOption(listJsonOption);
uninstallListCommand.AddOption(includeSystemOption);
uninstallListCommand.SetHandler(
    (asJson, includeSystem) =>
    {
        try
        {
            var apps = appDiscoveryService.DiscoverInstalledApps(includeSystem);
            CliRuntime.Output(apps, asJson, CliRuntime.FormatApps);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"App discovery failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    },
    listJsonOption,
    includeSystemOption);

var previewCommand = new Command("preview", "Builds an uninstall plan for a selected app.");
var appIdOption = new Option<string>("--app-id", "AppId from uninstall list output.") { IsRequired = true };
var manualRuleOption = new Option<string?>("--manual-rule", "Path to a ManualRule JSON file.");
var previewJsonOption = new Option<bool>("--json", "Print JSON output.");
var restorePointPlanOption = new Option<bool>("--restore-point", () => true, "Set planned restore-point flag in plan.");
previewCommand.AddOption(appIdOption);
previewCommand.AddOption(manualRuleOption);
previewCommand.AddOption(previewJsonOption);
previewCommand.AddOption(restorePointPlanOption);
previewCommand.SetHandler(
    async (appId, manualRulePath, asJson, restorePointPlanned) =>
    {
        try
        {
            var app = appDiscoveryService
                .DiscoverInstalledApps(includeSystemComponents: true)
                .FirstOrDefault(item => item.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase));

            if (app is null)
            {
                Console.Error.WriteLine($"AppId '{appId}' was not found.");
                Environment.ExitCode = 1;
                return;
            }

            var manualRule = CliRuntime.LoadManualRule(manualRulePath, jsonOptions);
            var plan = await uninstallPlanner
                .CreatePlanAsync(app, manualRule, restorePointPlanned)
                .ConfigureAwait(false);
            persistenceStore.SavePlan(plan);
            CliRuntime.Output(plan, asJson, CliRuntime.FormatPlanResult);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Preview failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    },
    appIdOption,
    manualRuleOption,
    previewJsonOption,
    restorePointPlanOption);

var executeCommand = new Command("execute", "Execute a previously generated uninstall plan.");
var planIdOption = new Option<string>("--plan-id", "PlanId from uninstall preview output.") { IsRequired = true };
var confirmOption = new Option<bool>("--confirm", "Required flag to execute.");
var dryRunOption = new Option<bool>("--dry-run", () => false, "Run execution without mutating system state.");
var executeRestorePointOption = new Option<bool>("--restore-point", () => true, "Attempt restore point creation when executing.");
var executeJsonOption = new Option<bool>("--json", "Print JSON output.");
executeCommand.AddOption(planIdOption);
executeCommand.AddOption(confirmOption);
executeCommand.AddOption(dryRunOption);
executeCommand.AddOption(executeRestorePointOption);
executeCommand.AddOption(executeJsonOption);
executeCommand.SetHandler(
    async (planId, confirm, dryRun, attemptRestorePoint, asJson) =>
    {
        try
        {
            if (!confirm)
            {
                Console.Error.WriteLine("Execution requires --confirm.");
                Environment.ExitCode = 1;
                return;
            }

            if (!persistenceStore.TryGetPlan(planId, out var plan) || plan is null)
            {
                Console.Error.WriteLine($"Plan '{planId}' not found.");
                Environment.ExitCode = 1;
                return;
            }

            var options = new UninstallExecutionOptions
            {
                Confirm = true,
                DryRun = dryRun,
                AttemptRestorePoint = attemptRestorePoint,
                AllowPartialWithoutAdmin = false
            };

            var report = await uninstallExecutor
                .ExecuteAsync(plan, options)
                .ConfigureAwait(false);

            persistenceStore.SaveReport(report);
            CliRuntime.Output(report, asJson, CliRuntime.FormatReportResult);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Execution failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    },
    planIdOption,
    confirmOption,
    dryRunOption,
    executeRestorePointOption,
    executeJsonOption);

var reportCommand = new Command("report", "Fetch latest report for a plan.");
var reportPlanIdOption = new Option<string>("--plan-id", "PlanId for report lookup.") { IsRequired = true };
var reportJsonOption = new Option<bool>("--json", "Print JSON output.");
reportCommand.AddOption(reportPlanIdOption);
reportCommand.AddOption(reportJsonOption);
reportCommand.SetHandler(
    (planId, asJson) =>
    {
        if (!persistenceStore.TryGetLatestReport(planId, out var report) || report is null)
        {
            Console.Error.WriteLine($"No report found for plan '{planId}'.");
            Environment.ExitCode = 1;
            return;
        }

        CliRuntime.Output(report, asJson, CliRuntime.FormatReportResult);
    },
    reportPlanIdOption,
    reportJsonOption);

uninstallCommand.AddCommand(uninstallListCommand);
uninstallCommand.AddCommand(previewCommand);
uninstallCommand.AddCommand(executeCommand);
uninstallCommand.AddCommand(reportCommand);

var configCommand = new Command("config", "Get or set mb defaults.");

var configGetCommand = new Command("get", "Get config values.");
var configGetKeyArgument = new Argument<string?>("key", () => null, "Optional key.");
var configGetJsonOption = new Option<bool>("--json", "Print JSON output.");
configGetCommand.AddArgument(configGetKeyArgument);
configGetCommand.AddOption(configGetJsonOption);
configGetCommand.SetHandler(
    (InvocationContext context) =>
    {
        var currentConfig = configStore.Load();
        var key = context.ParseResult.GetValueForArgument(configGetKeyArgument);
        var jsonProvided = context.ParseResult.FindResultFor(configGetJsonOption) is not null;
        var asJson = CliRuntime.ResolveJsonOutput(
            context.ParseResult.GetValueForOption(configGetJsonOption),
            jsonProvided,
            currentConfig);

        if (!string.IsNullOrWhiteSpace(key))
        {
            if (!configStore.TryGetValue(currentConfig, key, out var value))
            {
                Console.Error.WriteLine($"Unknown config key '{key}'. Known keys: {string.Join(", ", configStore.ListKeys())}");
                context.ExitCode = 1;
                return;
            }

            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { key = CliRuntime.NormalizeConfigKey(key), value }, jsonOptions));
            }
            else
            {
                Console.WriteLine($"{CliRuntime.NormalizeConfigKey(key)}={value}");
            }

            context.ExitCode = 0;
            return;
        }

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(currentConfig, jsonOptions));
        }
        else
        {
            foreach (var knownKey in configStore.ListKeys())
            {
                configStore.TryGetValue(currentConfig, knownKey, out var value);
                Console.WriteLine($"{knownKey}={value}");
            }
        }

        context.ExitCode = 0;
    });

var configSetCommand = new Command("set", "Set a config value.");
var configSetKeyArgument = new Argument<string>("key", "Config key.");
var configSetValueArgument = new Argument<string>("value", "Config value.");
configSetCommand.AddArgument(configSetKeyArgument);
configSetCommand.AddArgument(configSetValueArgument);
configSetCommand.SetHandler(
    (string key, string value) =>
    {
        var currentConfig = configStore.Load();
        if (!configStore.TrySet(currentConfig, key, value, out var updated, out var error))
        {
            Console.Error.WriteLine($"Config set failed: {error}");
            Environment.ExitCode = 1;
            return;
        }

        configStore.Save(updated);

        configStore.TryGetValue(updated, key, out var resolvedValue);
        Console.WriteLine($"{CliRuntime.NormalizeConfigKey(key)}={resolvedValue}");
    },
    configSetKeyArgument,
    configSetValueArgument);

configCommand.AddCommand(configGetCommand);
configCommand.AddCommand(configSetCommand);

var doctorCommand = new Command("doctor", "Check installation, environment, and connectivity.");
var doctorJsonOption = new Option<bool>("--json", "Print JSON output.");
doctorCommand.AddOption(doctorJsonOption);
doctorCommand.SetHandler(
    async (InvocationContext context) =>
    {
        var currentConfig = configStore.Load();
        var jsonProvided = context.ParseResult.FindResultFor(doctorJsonOption) is not null;
        var asJson = CliRuntime.ResolveJsonOutput(
            context.ParseResult.GetValueForOption(doctorJsonOption),
            jsonProvided,
            currentConfig);

        var exitCode = await CliRuntime.HandleDoctorAsync(asJson, configStore, persistenceStore, jsonOptions).ConfigureAwait(false);
        context.ExitCode = exitCode;
    });

root.AddCommand(geoCommand);
root.AddCommand(uninstallCommand);
root.AddCommand(ipCommand);
root.AddCommand(uiCommand);
root.AddCommand(configCommand);
root.AddCommand(doctorCommand);

return await root.InvokeAsync(args).ConfigureAwait(false);
