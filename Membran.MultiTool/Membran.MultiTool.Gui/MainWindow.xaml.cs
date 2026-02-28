using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Membran.MultiTool.Core.Models;
using Membran.MultiTool.Core.Security;
using Membran.MultiTool.Core.Services;
using Membran.MultiTool.Core.Windows;
using Membran.MultiTool.Geo.Providers;
using Membran.MultiTool.Geo.Services;
using Membran.MultiTool.Uninstall.Services;
using Microsoft.Win32;

namespace Membran.MultiTool.Gui;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();
    private readonly PersistenceStore _persistenceStore;
    private readonly ConfigStore _configStore;
    private readonly PathGuard _pathGuard;
    private readonly AppDiscoveryService _appDiscoveryService;
    private readonly UninstallPlanner _uninstallPlanner;
    private readonly UninstallExecutor _uninstallExecutor;
    private readonly GeoLookupService _geoLookupService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private List<DiscoveredApp> _allApps = [];
    private UninstallPlan? _currentPlan;

    public MainWindow()
    {
        InitializeComponent();

        _persistenceStore = new PersistenceStore();
        _configStore = new ConfigStore(_persistenceStore.RootDirectory);
        _pathGuard = new PathGuard();
        _appDiscoveryService = new AppDiscoveryService();

        var commandRunner = new SystemCommandRunner();
        _uninstallPlanner = new UninstallPlanner(_pathGuard, commandRunner);
        _uninstallExecutor = new UninstallExecutor(_pathGuard, commandRunner);

        _geoLookupService = new GeoLookupService(
        [
            new IpApiGeoProvider(_httpClient),
            new IpInfoGeoProvider(_httpClient)
        ]);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AllowedRootsTextBox.Text = string.Join(Environment.NewLine, _pathGuard.AllowedRoots);

        await RefreshAppsAsync();
        RefreshReports();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _httpClient.Dispose();
    }

    private async void LookupGeoButton_Click(object sender, RoutedEventArgs e)
    {
        LookupGeoButton.IsEnabled = false;
        GeoResultTextBlock.Text = "Looking up IP geolocation...";

        try
        {
            var query = new GeoQuery { InputIpOrSelf = GeoIpTextBox.Text.Trim() };
            var result = await _geoLookupService.LookupAsync(query);

            GeoResultTextBlock.Text = $"""
                Query IP:      {result.QueryIp}
                Provider:      {result.SourceProvider}
                Country:       {result.Country}
                Region:        {result.Region}
                City:          {result.City}
                Coordinates:   {result.Latitude}, {result.Longitude}
                ISP:           {result.Isp}
                Accuracy:      {result.AccuracyLabel}
                Confidence:    {result.Confidence:P0}
                Retrieved At:  {result.RetrievedAt:O}
                """;
        }
        catch (Exception ex)
        {
            GeoResultTextBlock.Text = $"Geo lookup failed: {ex.Message}";
        }
        finally
        {
            LookupGeoButton.IsEnabled = true;
        }
    }

    private void UseMyIpButton_Click(object sender, RoutedEventArgs e)
    {
        GeoIpTextBox.Text = "self";
    }

    private async void RefreshAppsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
    }

    private async Task RefreshAppsAsync()
    {
        RefreshAppsButton.IsEnabled = false;
        UninstallStatusTextBlock.Text = "Refreshing installed app list...";

        try
        {
            _allApps = _appDiscoveryService.DiscoverInstalledApps().ToList();
            ApplyAppFilter();
            UninstallStatusTextBlock.Text = $"Loaded {_allApps.Count} app(s).";
        }
        catch (Exception ex)
        {
            UninstallStatusTextBlock.Text = $"Failed to load apps: {ex.Message}";
        }
        finally
        {
            RefreshAppsButton.IsEnabled = true;
        }

        await Task.CompletedTask;
    }

    private void SearchAppsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAppFilter();
    }

    private void ApplyAppFilter()
    {
        var filter = SearchAppsTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            AppsDataGrid.ItemsSource = _allApps;
            return;
        }

        var filtered = _allApps.Where(app =>
                app.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                app.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                app.AppId.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AppsDataGrid.ItemsSource = filtered;
    }

    private void BrowseManualRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Manual Rule JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ManualRulePathTextBox.Text = dialog.FileName;
        }
    }

    private async void PreviewPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is not DiscoveredApp selectedApp)
        {
            UninstallStatusTextBlock.Text = "Select an app before building a preview plan.";
            return;
        }

        ManualRule manualRule;
        try
        {
            manualRule = LoadManualRuleOrDefault(ManualRulePathTextBox.Text.Trim());
        }
        catch (Exception ex)
        {
            UninstallStatusTextBlock.Text = $"Failed to load manual rule: {ex.Message}";
            return;
        }

        PreviewPlanButton.IsEnabled = false;
        try
        {
            _currentPlan = await _uninstallPlanner.CreatePlanAsync(
                selectedApp,
                manualRule,
                restorePointPlanned: RestorePointSettingCheckBox.IsChecked == true);

            _persistenceStore.SavePlan(_currentPlan);
            ArtifactsDataGrid.ItemsSource = _currentPlan.Artifacts;

            UninstallStatusTextBlock.Text =
                $"Preview created. PlanId={_currentPlan.PlanId}, Artifacts={_currentPlan.Artifacts.Length}, RequiresAdmin={_currentPlan.RequiresAdmin}";
        }
        catch (Exception ex)
        {
            UninstallStatusTextBlock.Text = $"Preview failed: {ex.Message}";
        }
        finally
        {
            PreviewPlanButton.IsEnabled = true;
        }
    }

    private async void ExecutePlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null)
        {
            UninstallStatusTextBlock.Text = "Build a preview plan before execution.";
            return;
        }

        if (ConfirmExecuteCheckBox.IsChecked != true)
        {
            UninstallStatusTextBlock.Text = "Execution requires enabling 'I confirm execution'.";
            return;
        }

        ExecutePlanButton.IsEnabled = false;
        try
        {
            var options = new UninstallExecutionOptions
            {
                Confirm = true,
                DryRun = DryRunSettingCheckBox.IsChecked == true,
                AttemptRestorePoint = RestorePointSettingCheckBox.IsChecked == true
            };

            var report = await _uninstallExecutor.ExecuteAsync(_currentPlan, options);
            _persistenceStore.SaveReport(report);
            RefreshReports();

            ReportDetailsTextBox.Text = JsonSerializer.Serialize(report, _jsonOptions);
            UninstallStatusTextBlock.Text =
                $"Execution finished. Removed={report.RemovedCount}, Failed={report.FailedCount}, PlanId={report.PlanId}";
        }
        catch (Exception ex)
        {
            UninstallStatusTextBlock.Text = $"Execution failed: {ex.Message}";
        }
        finally
        {
            ExecutePlanButton.IsEnabled = true;
        }
    }

    private void RefreshReportsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshReports();
    }

    private void RefreshReports()
    {
        var reports = _persistenceStore.ListReports()
            .OrderByDescending(report => report.CompletedAt)
            .ToList();

        ReportsDataGrid.ItemsSource = reports;
        if (reports.Count == 0)
        {
            ReportDetailsTextBox.Text = "No reports yet.";
        }
    }

    private void ReportsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportsDataGrid.SelectedItem is ExecutionReport selectedReport)
        {
            ReportDetailsTextBox.Text = JsonSerializer.Serialize(selectedReport, _jsonOptions);
        }
    }

    private ManualRule LoadManualRuleOrDefault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ManualRule.Empty;
        }

        var normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"Manual rule file not found: {normalized}");
        }

        var content = File.ReadAllText(normalized);
        return JsonSerializer.Deserialize<ManualRule>(content, _jsonOptions) ?? ManualRule.Empty;
    }

}
