namespace EngineeringQuantityAuditComparer.Core;

public interface IWorkbookImporter
{
    Task<CostWorkbook> ParseAsync(string path, WorkbookRole role, CancellationToken cancellationToken);
}

public interface IComparisonEngine
{
    ComparisonSession Compare(ComparisonRequest request, ComparisonSettings settings);

    // Temporary compatibility adapter for the pre-stage UI and exporter.
    ComparisonSession Compare(CostWorkbook submission, CostWorkbook review, ComparisonSettings settings);
}

public interface IReportExporter
{
    Task ExportAsync(ComparisonSession session, string templatePath, string outputPath, CancellationToken cancellationToken);
}
