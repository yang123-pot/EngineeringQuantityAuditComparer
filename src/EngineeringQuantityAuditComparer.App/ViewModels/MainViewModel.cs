using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EngineeringQuantityAuditComparer.Core;

namespace EngineeringQuantityAuditComparer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IWorkbookImporter _importer;
    private readonly IComparisonEngine _comparisonEngine;
    private readonly IReportExporter _exporter;
    private readonly List<UnitMatchDecision> _unitDecisions = [];
    private CancellationTokenSource? _cancellation;
    private string _submissionPath = string.Empty;
    private string _initialReviewPath = string.Empty;
    private string _finalReviewPath = string.Empty;
    private int _currentStep = 1;
    private bool _isBusy;
    private string _statusMessage = "请选择送审清单，并至少选择一份初审或终审清单。";
    private string _errorMessage = string.Empty;
    private ComparisonSession? _session;
    private decimal _amountTolerance = 0.01m;
    private decimal _quantityTolerance = 0.0001m;
    private UnitMatchRowViewModel? _selectedUnitMatchRow;

    public MainViewModel(IWorkbookImporter importer, IComparisonEngine comparisonEngine, IReportExporter exporter)
    {
        _importer = importer;
        _comparisonEngine = comparisonEngine;
        _exporter = exporter;
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanAnalyze);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        MoveInitialUpCommand = new RelayCommand(
            () => MoveSelectedUnit(WorkbookRole.InitialReview, -1),
            () => CanMoveSelectedUnit(WorkbookRole.InitialReview, -1));
        MoveInitialDownCommand = new RelayCommand(
            () => MoveSelectedUnit(WorkbookRole.InitialReview, 1),
            () => CanMoveSelectedUnit(WorkbookRole.InitialReview, 1));
        MoveFinalUpCommand = new RelayCommand(
            () => MoveSelectedUnit(WorkbookRole.FinalReview, -1),
            () => CanMoveSelectedUnit(WorkbookRole.FinalReview, -1));
        MoveFinalDownCommand = new RelayCommand(
            () => MoveSelectedUnit(WorkbookRole.FinalReview, 1),
            () => CanMoveSelectedUnit(WorkbookRole.FinalReview, 1));
        ConfirmAllCommand = new RelayCommand(
            ConfirmAllExceptions,
            () => Session is not null && CurrentStep == 2 && !IsBusy);
    }

    public string SubmissionPath
    {
        get => _submissionPath;
        set
        {
            if (SetProperty(ref _submissionPath, value))
            {
                ResetAnalysisForFileChange();
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string InitialReviewPath
    {
        get => _initialReviewPath;
        set
        {
            if (SetProperty(ref _initialReviewPath, value))
            {
                ResetAnalysisForFileChange();
                OnPropertyChanged(nameof(ReviewPath));
                OnPropertyChanged(nameof(HasInitialReview));
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FinalReviewPath
    {
        get => _finalReviewPath;
        set
        {
            if (SetProperty(ref _finalReviewPath, value))
            {
                ResetAnalysisForFileChange();
                OnPropertyChanged(nameof(HasFinalReview));
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Temporary source compatibility for MainWindow.xaml.cs until Task 5 migrates its picker.
    public string ReviewPath
    {
        get => InitialReviewPath;
        set => InitialReviewPath = value;
    }

    public bool HasInitialReview => !string.IsNullOrWhiteSpace(InitialReviewPath);
    public bool HasFinalReview => !string.IsNullOrWhiteSpace(FinalReviewPath);

    public int CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsFileStep));
                OnPropertyChanged(nameof(IsExceptionStep));
                OnPropertyChanged(nameof(IsPreviewStep));
                ConfirmAllCommand.NotifyCanExecuteChanged();
                NotifyMoveCommands();
            }
        }
    }

    public bool IsFileStep => CurrentStep == 1;
    public bool IsExceptionStep => CurrentStep == 2;
    public bool IsPreviewStep => CurrentStep == 3;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                ConfirmAllCommand.NotifyCanExecuteChanged();
                NotifyMoveCommands();
                OnPropertyChanged(nameof(CanContinueToPreview));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public decimal AmountTolerance
    {
        get => _amountTolerance;
        set => SetProperty(ref _amountTolerance, value < 0 ? 0 : value);
    }

    public decimal QuantityTolerance
    {
        get => _quantityTolerance;
        set => SetProperty(ref _quantityTolerance, value < 0 ? 0 : value);
    }

    public ComparisonSession? Session
    {
        get => _session;
        private set
        {
            if (SetProperty(ref _session, value))
            {
                OnPropertyChanged(nameof(CanContinueToPreview));
            }
        }
    }

    public ObservableCollection<UnitMatchRowViewModel> UnitMatchRows { get; } = [];
    public ObservableCollection<ItemExceptionViewModel> ItemExceptions { get; } = [];
    public ObservableCollection<UnitPreviewViewModel> UnitPreviews { get; } = [];
    public ObservableCollection<DifferencePreviewViewModel> DifferencePreviews { get; } = [];

    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ConfirmAllCommand { get; }
    public IRelayCommand MoveInitialUpCommand { get; }
    public IRelayCommand MoveInitialDownCommand { get; }
    public IRelayCommand MoveFinalUpCommand { get; }
    public IRelayCommand MoveFinalDownCommand { get; }

    public UnitMatchRowViewModel? SelectedUnitMatchRow
    {
        get => _selectedUnitMatchRow;
        set
        {
            if (!SetProperty(ref _selectedUnitMatchRow, value))
            {
                return;
            }

            NotifyMoveCommands();
        }
    }

    public bool CanContinueToPreview =>
        Session is not null && PendingCount == 0 && !IsBusy;

    public async Task AnalyzeAsync()
    {
        if (!CanAnalyze())
        {
            ErrorMessage = "请选择送审清单和至少一份初审或终审清单；文件须为不同的 .xls 或 .xlsx 文件。";
            return;
        }

        if (TryGetMisassignment(out var misassignmentMessage))
        {
            ErrorMessage = misassignmentMessage;
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _unitDecisions.Clear();
        Session = null;
        ClearResults();
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在并行读取所选清单…";
        try
        {
            var submissionTask = _importer.ParseAsync(SubmissionPath, WorkbookRole.Submission, _cancellation.Token);
            var initialTask = ParseOptionalAsync(InitialReviewPath, WorkbookRole.InitialReview, _cancellation.Token);
            var finalTask = ParseOptionalAsync(FinalReviewPath, WorkbookRole.FinalReview, _cancellation.Token);
            await Task.WhenAll((Task)submissionTask, initialTask, finalTask).ConfigureAwait(true);

            StatusMessage = "正在自动匹配单位工程和清单项目…";
            Session = _comparisonEngine.Compare(
                new ComparisonRequest(submissionTask.Result, initialTask.Result, finalTask.Result),
                CurrentSettings());
            PopulateResults(Session, rebuildUnitDraft: true);
            CurrentStep = 2;
            StatusMessage = $"自动匹配完成：共 {UnitMatchRows.Count} 个单位工程，" +
                $"有 {PendingUnitCount} 个单位工程需要确认。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "处理失败，请根据提示检查文件。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ConfirmAllExceptions()
    {
        if (Session is null)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (PendingUnitCount > 0)
        {
            ErrorMessage = $"仍有 {PendingUnitCount} 个单位工程需要人工确认。";
            StatusMessage = "匹配尚未确认，暂不能进入预览。";
            return;
        }

        if (!UnitMatchDraftService.TryValidateCompleteness(UnitMatchRows, Session.Request, out var draftError))
        {
            ErrorMessage = draftError;
            return;
        }

        try
        {
            _unitDecisions.Clear();
            _unitDecisions.AddRange(UnitMatchDraftService.CreateDecisions(UnitMatchRows, Session.Request));
            var recalculated = _comparisonEngine.Compare(Session.Request, CurrentSettings());
            var matchesDraft = UnitMatchDraftService.MatchesSession(UnitMatchRows, recalculated, out var mismatch);
            if (recalculated.UnitConfirmations.Count > 0 || !matchesDraft)
            {
                ErrorMessage = recalculated.UnitConfirmations.Count > 0
                    ? "匹配重算后仍存在单位工程歧义，已保留当前页面，请重新检查。"
                    : mismatch;
                StatusMessage = "确认未能应用，请检查单位工程配对。";
                return;
            }

            Session = recalculated;
            PopulateResults(Session, rebuildUnitDraft: false);
            if (PendingCount == 0)
            {
                CurrentStep = 3;
                StatusMessage = "单位工程匹配已确认，可以预览并导出。";
            }
            else
            {
                CurrentStep = 2;
                StatusMessage = $"仍有 {PendingUnitCount} 个单位工程需要确认。";
            }
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "确认未能应用，请检查所选关联。";
        }
    }

    public async Task ExportAsync(string outputPath, string templatePath)
    {
        if (Session is null)
        {
            ErrorMessage = "请先完成清单分析。";
            return;
        }

        if (PendingCount > 0)
        {
            ErrorMessage = "仍有异常未确认，暂不能导出。";
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "正在生成 Excel 分析报告…";
        try
        {
            await _exporter.ExportAsync(Session, templatePath, outputPath, _cancellation.Token).ConfigureAwait(true);
            StatusMessage = $"已导出：{outputPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "导出已取消。";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "导出失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BackToFiles()
    {
        CurrentStep = 1;
        StatusMessage = "可重新选择文件并分析。";
    }

    private int PendingUnitCount => UnitMatchRows.Count(x => x.IsBlocking);
    private int PendingCount => PendingUnitCount;

    private bool CanAnalyze()
    {
        if (IsBusy || !TryNormalizeWorkbookPath(SubmissionPath, out var submission) ||
            (!HasInitialReview && !HasFinalReview))
        {
            return false;
        }

        var selectedPaths = new List<string> { submission };
        if (HasInitialReview)
        {
            if (!TryNormalizeWorkbookPath(InitialReviewPath, out var initial))
            {
                return false;
            }

            selectedPaths.Add(initial);
        }

        if (HasFinalReview)
        {
            if (!TryNormalizeWorkbookPath(FinalReviewPath, out var final))
            {
                return false;
            }

            selectedPaths.Add(final);
        }

        return selectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == selectedPaths.Count;
    }

    private void Cancel() => _cancellation?.Cancel();

    private void ResetAnalysisForFileChange()
    {
        if (Session is null && UnitMatchRows.Count == 0)
        {
            return;
        }

        _unitDecisions.Clear();
        Session = null;
        ClearResults();
        CurrentStep = 1;
        ErrorMessage = string.Empty;
        StatusMessage = "文件已变化，请重新开始自动分析。";
    }

    private ComparisonSettings CurrentSettings() => new(AmountTolerance, QuantityTolerance)
    {
        UnitDecisions = _unitDecisions.ToList()
    };

    private async Task<CostWorkbook?> ParseOptionalAsync(
        string path,
        WorkbookRole role,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : await _importer.ParseAsync(path, role, cancellationToken).ConfigureAwait(true);

    private void PopulateResults(ComparisonSession session, bool rebuildUnitDraft)
    {
        if (rebuildUnitDraft)
        {
            ClearUnitMatchRows();
            foreach (var row in UnitMatchDraftService.Build(session))
            {
                row.PropertyChanged += UnitMatchRowPropertyChanged;
                UnitMatchRows.Add(row);
            }

            SelectedUnitMatchRow = null;
        }

        ItemExceptions.Clear();
        var activeStages = ActiveStages(session.Request);
        var activePairs = ComparisonPairs.OutputFor(session.Request);

        UnitPreviews.Clear();
        foreach (var unit in session.UnitComparisons)
        {
            var stageAmounts = activeStages.ToDictionary(
                stage => stage,
                stage => unit.Rows.Sum(row => row.Total(stage)));
            var differences = activePairs.ToDictionary(
                pair => pair.Kind,
                pair => ComparisonPairs.OutputDifference(pair, stage => stageAmounts[stage]));
            var exceptionCount = session.UnitConfirmations.Count(x => x.UnitProjectName == unit.Name);
            UnitPreviews.Add(new UnitPreviewViewModel(unit.Name, stageAmounts, differences, exceptionCount));
        }

        DifferencePreviews.Clear();
        foreach (var item in session.UnitComparisons
                     .SelectMany(unit => unit.Rows.Select(row => new
                     {
                         Unit = unit.Name,
                         Row = row,
                         Score = activePairs.Select(pair => Math.Abs(ComparisonPairs.OutputDifference(pair, row.Total)))
                             .DefaultIfEmpty(0m)
                             .Max()
                     }))
                     .OrderByDescending(x => x.Score)
                     .Take(200))
        {
            DifferencePreviews.Add(new DifferencePreviewViewModel(item.Unit, item.Row, activeStages, activePairs));
        }

        ConfirmAllCommand.NotifyCanExecuteChanged();
        NotifyMoveCommands();
        OnPropertyChanged(nameof(CanContinueToPreview));
    }

    private void ClearResults()
    {
        ClearUnitMatchRows();
        SelectedUnitMatchRow = null;
        ItemExceptions.Clear();
        UnitPreviews.Clear();
        DifferencePreviews.Clear();
        ConfirmAllCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanContinueToPreview));
    }

    private void ClearUnitMatchRows()
    {
        foreach (var row in UnitMatchRows)
        {
            row.PropertyChanged -= UnitMatchRowPropertyChanged;
        }

        UnitMatchRows.Clear();
    }

    private bool CanMoveSelectedUnit(WorkbookRole stage, int offset)
    {
        if (IsBusy || CurrentStep != 2 || Session is null || SelectedUnitMatchRow?.Unit(stage) is null)
        {
            return false;
        }

        var index = UnitMatchRows.IndexOf(SelectedUnitMatchRow);
        return index >= 0 && index + offset >= 0 && index + offset < UnitMatchRows.Count;
    }

    private void MoveSelectedUnit(WorkbookRole stage, int offset)
    {
        if (Session is null || SelectedUnitMatchRow is null)
        {
            return;
        }

        var rowsBeforeMove = UnitMatchRows.ToList();
        var moved = SelectedUnitMatchRow.Unit(stage);
        if (!UnitMatchDraftService.TryMove(
                UnitMatchRows,
                SelectedUnitMatchRow,
                stage,
                offset,
                Session.Request,
                out var error))
        {
            ErrorMessage = error;
            return;
        }

        foreach (var removed in rowsBeforeMove.Except(UnitMatchRows))
        {
            removed.PropertyChanged -= UnitMatchRowPropertyChanged;
        }

        SelectedUnitMatchRow = UnitMatchRows.Single(x => ReferenceEquals(x.Unit(stage), moved));
        ErrorMessage = string.Empty;
        NotifyMoveCommands();
        OnPropertyChanged(nameof(CanContinueToPreview));
    }

    private void NotifyMoveCommands()
    {
        MoveInitialUpCommand.NotifyCanExecuteChanged();
        MoveInitialDownCommand.NotifyCanExecuteChanged();
        MoveFinalUpCommand.NotifyCanExecuteChanged();
        MoveFinalDownCommand.NotifyCanExecuteChanged();
    }

    private void UnitMatchRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(UnitMatchRowViewModel.IsBlocking) or
            nameof(UnitMatchRowViewModel.IsConfirmed) or
            nameof(UnitMatchRowViewModel.RequiresConfirmation))
        {
            OnPropertyChanged(nameof(CanContinueToPreview));
        }
    }

    private bool TryGetMisassignment(out string message)
    {
        var submissionName = Path.GetFileNameWithoutExtension(SubmissionPath);
        var initialName = Path.GetFileNameWithoutExtension(InitialReviewPath);
        var finalName = Path.GetFileNameWithoutExtension(FinalReviewPath);
        var submissionLooksAudited = ContainsAny(submissionName, "初审", "终审");

        if (submissionLooksAudited && HasInitialReview && ContainsAny(initialName, "送审", "报审"))
        {
            message = "送审文件与初审文件疑似选反，请重新选择这两个阶段。";
            return true;
        }

        if (submissionLooksAudited && HasFinalReview && ContainsAny(finalName, "送审", "报审"))
        {
            message = "送审文件与终审文件疑似选反，请重新选择这两个阶段。";
            return true;
        }

        if (HasInitialReview && HasFinalReview &&
            initialName.Contains("终审", StringComparison.OrdinalIgnoreCase) &&
            finalName.Contains("初审", StringComparison.OrdinalIgnoreCase))
        {
            message = "初审文件与终审文件疑似选反，请重新选择这两个阶段。";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool TryNormalizeWorkbookPath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var candidate = path.Trim();
            var extension = Path.GetExtension(candidate);
            if (!extension.Equals(".xls", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }
    }

    private static IReadOnlyList<WorkbookRole> ActiveStages(ComparisonRequest request)
    {
        var stages = new List<WorkbookRole>(3) { WorkbookRole.Submission };
        if (request.HasInitialReview)
        {
            stages.Add(WorkbookRole.InitialReview);
        }

        if (request.HasFinalReview)
        {
            stages.Add(WorkbookRole.FinalReview);
        }

        return stages;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
