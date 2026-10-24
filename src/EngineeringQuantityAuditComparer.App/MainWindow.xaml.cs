using System.ComponentModel;
using System.IO;
using System.Windows;
using EngineeringQuantityAuditComparer.App.ViewModels;
using EngineeringQuantityAuditComparer.Core;
using EngineeringQuantityAuditComparer.Infrastructure;
using Microsoft.Win32;

namespace EngineeringQuantityAuditComparer.App;

public partial class MainWindow : Window
{
    private const string ExcelFilter = "Excel 清单 (*.xls;*.xlsx)|*.xls;*.xlsx|所有文件 (*.*)|*.*";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new WorkbookImporter(), new ComparisonEngine(), new ReportExporter());
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyStageColumnVisibility();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void BrowseSubmission_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExcelFile("选择送审工程量清单");
        if (path is not null)
        {
            ViewModel.SubmissionPath = path;
        }
    }

    private void BrowseInitialReview_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExcelFile("选择审计初审工程量清单");
        if (path is not null)
        {
            ViewModel.InitialReviewPath = path;
        }
    }

    private void BrowseFinalReview_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExcelFile("选择审计终审工程量清单");
        if (path is not null)
        {
            ViewModel.FinalReviewPath = path;
        }
    }

    private static string? PickExcelFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ExcelFilter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void FileDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FileDrop_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        var path = files[0];
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".xls", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("请拖入 .xls 或 .xlsx 清单文件。", "文件格式不支持", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        switch (sender is FrameworkElement element ? element.Tag as string : null)
        {
            case "Submission":
                ViewModel.SubmissionPath = path;
                break;
            case "InitialReview":
                ViewModel.InitialReviewPath = path;
                break;
            case "FinalReview":
                ViewModel.FinalReviewPath = path;
                break;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasInitialReview) or
            nameof(MainViewModel.HasFinalReview) or
            nameof(MainViewModel.Session))
        {
            ApplyStageColumnVisibility();
        }
    }

    private void ApplyStageColumnVisibility()
    {
        var hasInitial = ViewModel.Session?.Request.HasInitialReview ?? ViewModel.HasInitialReview;
        var hasFinal = ViewModel.Session?.Request.HasFinalReview ?? ViewModel.HasFinalReview;
        var activeDifferences = ViewModel.Session is null
            ? []
            : ComparisonPairs.OutputFor(ViewModel.Session.Request).Select(x => x.Kind).ToList();

        UnitMatchInitialColumn.Visibility = hasInitial ? Visibility.Visible : Visibility.Collapsed;
        UnitMatchFinalColumn.Visibility = hasFinal ? Visibility.Visible : Visibility.Collapsed;
        InitialMoveButtons.Visibility = hasInitial ? Visibility.Visible : Visibility.Collapsed;
        FinalMoveButtons.Visibility = hasFinal ? Visibility.Visible : Visibility.Collapsed;

        UnitInitialAmountColumn.Visibility = hasInitial ? Visibility.Visible : Visibility.Collapsed;
        UnitFinalAmountColumn.Visibility = hasFinal ? Visibility.Visible : Visibility.Collapsed;
        UnitInitialVsSubmissionColumn.Visibility = activeDifferences.Contains(DifferenceKind.InitialVsSubmission)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UnitFinalVsSubmissionColumn.Visibility = activeDifferences.Contains(DifferenceKind.FinalVsSubmission)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ItemInitialTotalColumn.Visibility = hasInitial ? Visibility.Visible : Visibility.Collapsed;
        ItemFinalTotalColumn.Visibility = hasFinal ? Visibility.Visible : Visibility.Collapsed;
        ItemInitialVsSubmissionColumn.Visibility = activeDifferences.Contains(DifferenceKind.InitialVsSubmission)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ItemFinalVsSubmissionColumn.Visibility = activeDifferences.Contains(DifferenceKind.FinalVsSubmission)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Session is null)
        {
            return;
        }

        var projectName = SanitizeFileName(ViewModel.Session.Submission.ProjectName);
        var dialog = new SaveFileDialog
        {
            Title = "导出对比表和审减分析报告",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            FileName = $"{projectName}-审减分析-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "ReportTemplate.xlsx");
        await ViewModel.ExportAsync(dialog.FileName, templatePath);
    }

    private void BackToFiles_Click(object sender, RoutedEventArgs e) => ViewModel.BackToFiles();

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "项目" : result;
    }
}
