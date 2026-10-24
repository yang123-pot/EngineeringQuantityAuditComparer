using System.Data;
using System.Globalization;
using System.Text;
using EngineeringQuantityAuditComparer.Core;
using ExcelDataReader;

namespace EngineeringQuantityAuditComparer.Infrastructure;

public sealed class WorkbookImporter : IWorkbookImporter
{
    static WorkbookImporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Task<CostWorkbook> ParseAsync(string path, WorkbookRole role, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请选择清单文件。", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到所选清单文件。", path);
        }

        return Task.Run(() => Parse(path, role, cancellationToken), cancellationToken);
    }

    private static CostWorkbook Parse(string path, WorkbookRole role, CancellationToken cancellationToken)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration
        {
            FallbackEncoding = Encoding.GetEncoding(936),
            LeaveOpen = false
        });
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        var summaries = new List<TaxInclusiveSummaryItem>();
        var units = new List<UnitProject>();
        var projectName = Path.GetFileNameWithoutExtension(path);
        var rolePrefix = $"（{RoleName(role)}）";
        if (projectName.StartsWith(rolePrefix, StringComparison.Ordinal))
        {
            projectName = projectName[rolePrefix.Length..].Trim();
        }

        for (var tableIndex = 0; tableIndex < dataSet.Tables.Count; tableIndex++)
        {
            var table = dataSet.Tables[tableIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var titleText = FirstRowsText(table, 8);
            if (titleText.Contains("单项工程投标报价汇总表", StringComparison.Ordinal) ||
                titleText.Contains("单项工程汇总表", StringComparison.Ordinal))
            {
                projectName = ExtractProjectName(table) ?? projectName;
                summaries.AddRange(ParseSummary(table));
            }
            else if (titleText.Contains("分部分项工程和单价措施项目清单与计价表", StringComparison.Ordinal) ||
                     titleText.Contains("分部分项工程项目清单计价表", StringComparison.Ordinal))
            {
                units.Add(ParseUnitProject(table, role, tableIndex,
                    titleText.Contains("分部分项工程项目清单计价表", StringComparison.Ordinal)));
            }
        }

        if (summaries.Count == 0)
        {
            throw new InvalidDataException($"{RoleName(role)}文件中未识别到单项工程汇总表，请确认文件是否选反或格式是否正确。");
        }

        if (units.Count == 0)
        {
            throw new InvalidDataException($"{RoleName(role)}文件中未识别到单位工程清单页。");
        }

        return new CostWorkbook(path, projectName, summaries, units);
    }

    private static IReadOnlyList<TaxInclusiveSummaryItem> ParseSummary(DataTable table)
    {
        var title = FirstRowsText(table, 3);
        var updatedFormat = title.Contains("单项工程汇总表", StringComparison.Ordinal) &&
            !title.Contains("单项工程投标报价汇总表", StringComparison.Ordinal);
        var headerRow = FindRow(table, row => row.Any(cell => updatedFormat
            ? TextNormalizer.Normalize(CellText(cell)) == "项目内容"
            : IsSummaryNameHeader(CellText(cell))));
        if (headerRow < 0)
        {
            return [];
        }

        var nameColumn = updatedFormat ? FindColumn(table.Rows[headerRow], "项目内容")
            : FindColumn(table.Rows[headerRow], "单位工程名称");
        if (nameColumn < 0 && !updatedFormat)
        {
            nameColumn = FindColumn(table.Rows[headerRow], "单项工程名称");
        }
        var amountColumn = FindSummaryAmountColumn(table, headerRow, nameColumn);
        var result = new List<TaxInclusiveSummaryItem>();
        for (var rowIndex = headerRow + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var name = CellText(Cell(row, nameColumn)).Trim();
            if (updatedFormat && !TryDecimal(Cell(row, 0), out _))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(name) || name.Contains("合计", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryDecimal(Cell(row, amountColumn), out var amount))
            {
                result.Add(new TaxInclusiveSummaryItem(name, amount));
            }
        }

        return result;
    }

    private static UnitProject ParseUnitProject(DataTable table, WorkbookRole role, int sourceSheetIndex, bool updatedFormat)
    {
        var headerRow = FindRow(table, row =>
            row.Any(cell => CellText(cell).Contains("项目编码", StringComparison.Ordinal)) &&
            row.Any(cell => CellText(cell).Contains("项目名称", StringComparison.Ordinal)));
        if (headerRow < 0)
        {
            throw new InvalidDataException(
                $"{RoleName(role)}文件的工作表“{table.TableName}”未找到包含“项目编码”和“项目名称”的清单表头。");
        }

        var projectName = ExtractProjectName(table, headerRow);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidDataException($"{RoleName(role)}文件的工作表“{table.TableName}”未找到表头工程名称。");
        }

        var codeColumn = FindColumn(table.Rows[headerRow], "项目编码");
        var sequenceColumn = FindColumn(table.Rows[headerRow], "序号");
        var nameColumn = FindColumn(table.Rows[headerRow], "项目名称");
        var featureColumn = FindColumn(table.Rows[headerRow], "项目特征");
        var unitColumn = FindColumn(table.Rows[headerRow], "计量单位");
        var quantityColumn = FindColumn(table.Rows[headerRow], "工程量");
        var unitPriceColumn = quantityColumn < 0
            ? -1
            : FindSubHeaderColumn(table, headerRow, "综合单价", quantityColumn + 1);
        var totalColumn = unitPriceColumn < 0
            ? -1
            : FindSubHeaderColumn(table, headerRow, "合价", unitPriceColumn + 1);
        var missingColumns = new List<string>();
        AddMissingColumn(missingColumns, sequenceColumn, "序号");
        AddMissingColumn(missingColumns, codeColumn, "项目编码");
        AddMissingColumn(missingColumns, nameColumn, "项目名称");
        AddMissingColumn(missingColumns, featureColumn, "项目特征");
        AddMissingColumn(missingColumns, unitColumn, "计量单位");
        AddMissingColumn(missingColumns, quantityColumn, "工程量");
        AddMissingColumn(missingColumns, unitPriceColumn, "综合单价");
        AddMissingColumn(missingColumns, totalColumn, "合价");
        if (missingColumns.Count > 0)
        {
            throw new InvalidDataException(
                $"{RoleName(role)}文件的工作表“{table.TableName}”清单表头不完整，缺少：{string.Join('、', missingColumns)}。");
        }

        var items = new List<BillItem>();
        var parsedSections = new List<BillSection>();
        var openSections = new Stack<SectionFrame>();
        BillSection? rootSection = null;
        var hasExplicitTitle = false;
        var hasUnmatchedSectionSubtotal = false;
        var passedRootTotal = false;
        SectionFrame? updatedSection = null;
        decimal updatedSectionTotal = 0m;
        void CloseUpdatedSection()
        {
            if (updatedSection is null)
            {
                return;
            }

            parsedSections.Add(new BillSection(
                updatedSection.Title,
                1,
                updatedSection.SourceRowIndex,
                updatedSectionTotal,
                updatedSection.DirectItemSourceRows,
                IsRoot: false));
            updatedSection = null;
        }

        for (var rowIndex = headerRow + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var code = CellText(Cell(row, codeColumn)).Trim();
            var name = CellText(Cell(row, nameColumn)).Trim();
            var structuralLabel = TextNormalizer.Normalize(name);
            if (updatedFormat && TextNormalizer.Normalize(CellText(Cell(row, sequenceColumn))) == "合计" &&
                string.IsNullOrWhiteSpace(name) && TryDecimal(Cell(row, totalColumn), out var updatedRootTotal))
            {
                CloseUpdatedSection();
                rootSection = new BillSection("整个工程", 0, rowIndex, updatedRootTotal, [], IsRoot: true);
                break;
            }

            if (IsStructuralTotal(structuralLabel))
            {
                if (structuralLabel == "分部小计")
                {
                    if (openSections.Count == 0)
                    {
                        hasUnmatchedSectionSubtotal = true;
                        continue;
                    }

                    var frame = openSections.Pop();
                    parsedSections.Add(new BillSection(
                        frame.Title,
                        frame.Level,
                        frame.SourceRowIndex,
                        DecimalOrZero(Cell(row, totalColumn)),
                        frame.DirectItemSourceRows,
                        IsRoot: false));
                }
                else if (structuralLabel == "分部分项合计")
                {
                    rootSection = new BillSection(
                        "整个工程",
                        0,
                        rowIndex,
                        DecimalOrZero(Cell(row, totalColumn)),
                        [],
                        IsRoot: true);
                    if (hasExplicitTitle)
                    {
                        break;
                    }

                    passedRootTotal = true;
                }

                continue;
            }

            if (updatedFormat && IsUpdatedSectionRow(row, sequenceColumn, codeColumn, nameColumn,
                featureColumn, unitColumn, quantityColumn, unitPriceColumn, totalColumn))
            {
                CloseUpdatedSection();
                updatedSection = new SectionFrame(name, 1, rowIndex);
                updatedSectionTotal = DecimalOrZero(Cell(row, totalColumn));
                hasExplicitTitle = true;
                continue;
            }

            if (!updatedFormat && !passedRootTotal && IsExplicitTitleRow(
                row,
                sequenceColumn,
                codeColumn,
                nameColumn,
                featureColumn,
                unitColumn,
                quantityColumn,
                unitPriceColumn,
                totalColumn))
            {
                openSections.Push(new SectionFrame(name, openSections.Count + 1, rowIndex));
                hasExplicitTitle = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(name) ||
                (string.IsNullOrWhiteSpace(code) && !HasItemEvidence(row, sequenceColumn,
                    featureColumn, unitColumn, quantityColumn, unitPriceColumn, totalColumn)))
            {
                continue;
            }

            var quantity = DecimalOrZero(Cell(row, quantityColumn));
            var unitPrice = DecimalOrZero(Cell(row, unitPriceColumn));
            var total = DecimalOrZero(Cell(row, totalColumn));
            var item = new BillItem(
                NullIfWhiteSpace(CellText(Cell(row, sequenceColumn))),
                code,
                name,
                CellText(Cell(row, featureColumn)).Trim(),
                CellText(Cell(row, unitColumn)).Trim(),
                quantity,
                quantity,
                unitPrice,
                total)
            {
                SourceRowIndex = rowIndex
            };
            items.Add(item);
            if (openSections.Count > 0)
            {
                openSections.Peek().DirectItemSourceRows.Add(rowIndex);
            }
            else
            {
                updatedSection?.DirectItemSourceRows.Add(rowIndex);
            }
        }

        CloseUpdatedSection();

        if (openSections.Count > 0)
        {
            var open = openSections.Peek();
            throw new InvalidDataException(
                $"{RoleName(role)}文件的工作表“{table.TableName}”中小标题“{open.Title}”未找到对应的分部小计。");
        }

        if (hasExplicitTitle && hasUnmatchedSectionSubtotal)
        {
            throw new InvalidDataException(
                $"{RoleName(role)}文件的工作表“{table.TableName}”出现没有对应小标题的分部小计。");
        }

        var sections = new List<BillSection>();
        if (rootSection is not null)
        {
            sections.Add(rootSection);
        }

        sections.AddRange(parsedSections.OrderBy(x => x.SourceRowIndex));
        return CreateUnitProject(projectName, table, sourceSheetIndex, items, sections);
    }

    private static void AddMissingColumn(ICollection<string> missingColumns, int column, string name)
    {
        if (column < 0)
        {
            missingColumns.Add(name);
        }
    }

    private static UnitProject CreateUnitProject(
        string projectName,
        DataTable table,
        int sourceSheetIndex,
        IReadOnlyList<BillItem> items,
        IReadOnlyList<BillSection> sections) =>
        new(projectName, DetectKind(projectName), items)
        {
            SourceSheetIndex = sourceSheetIndex,
            SourceSheetName = table.TableName,
            Sections = sections
        };

    private static bool IsExplicitTitleRow(
        DataRow row,
        int sequenceColumn,
        int codeColumn,
        int nameColumn,
        int featureColumn,
        int unitColumn,
        int quantityColumn,
        int unitPriceColumn,
        int totalColumn) =>
        string.IsNullOrWhiteSpace(CellText(Cell(row, sequenceColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, codeColumn))) &&
        !string.IsNullOrWhiteSpace(CellText(Cell(row, nameColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, featureColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, unitColumn))) &&
        !TryDecimal(Cell(row, quantityColumn), out _) &&
        !TryDecimal(Cell(row, unitPriceColumn), out _) &&
        !TryDecimal(Cell(row, totalColumn), out _);

    private static bool IsUpdatedSectionRow(
        DataRow row,
        int sequenceColumn,
        int codeColumn,
        int nameColumn,
        int featureColumn,
        int unitColumn,
        int quantityColumn,
        int unitPriceColumn,
        int totalColumn) =>
        string.IsNullOrWhiteSpace(CellText(Cell(row, sequenceColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, codeColumn))) &&
        !string.IsNullOrWhiteSpace(CellText(Cell(row, nameColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, featureColumn))) &&
        string.IsNullOrWhiteSpace(CellText(Cell(row, unitColumn))) &&
        !TryDecimal(Cell(row, quantityColumn), out _) &&
        !TryDecimal(Cell(row, unitPriceColumn), out _) &&
        TryDecimal(Cell(row, totalColumn), out _);

    private static bool HasItemEvidence(
        DataRow row,
        int sequenceColumn,
        int featureColumn,
        int unitColumn,
        int quantityColumn,
        int unitPriceColumn,
        int totalColumn) =>
        !string.IsNullOrWhiteSpace(CellText(Cell(row, sequenceColumn))) ||
        !string.IsNullOrWhiteSpace(CellText(Cell(row, featureColumn))) ||
        !string.IsNullOrWhiteSpace(CellText(Cell(row, unitColumn))) ||
        TryDecimal(Cell(row, quantityColumn), out _) ||
        TryDecimal(Cell(row, unitPriceColumn), out _) ||
        TryDecimal(Cell(row, totalColumn), out _);

    private static bool IsStructuralTotal(string normalizedLabel) => normalizedLabel is
        "分部小计" or "分部分项合计" or "单价措施合计";

    private static int FindSummaryAmountColumn(DataTable table, int headerRow, int nameColumn)
    {
        for (var column = nameColumn + 1; column < table.Columns.Count; column++)
        {
            var combined = string.Concat(
                CellText(Cell(table.Rows[headerRow], column)),
                headerRow + 1 < table.Rows.Count ? CellText(Cell(table.Rows[headerRow + 1], column)) : string.Empty);
            if (combined.Contains("金额", StringComparison.Ordinal) || combined.Contains("造价", StringComparison.Ordinal))
            {
                return column;
            }
        }

        return Math.Min(nameColumn + 1, table.Columns.Count - 1);
    }

    private static int FindSubHeaderColumn(
        DataTable table,
        int headerRow,
        string expected,
        int minimumColumn)
    {
        var normalizedExpected = TextNormalizer.Normalize(expected);
        for (var rowIndex = headerRow; rowIndex <= Math.Min(headerRow + 2, table.Rows.Count - 1); rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = Math.Max(minimumColumn, 0); columnIndex < row.ItemArray.Length; columnIndex++)
            {
                if (TextNormalizer.Normalize(CellText(row[columnIndex]))
                    .Contains(normalizedExpected, StringComparison.Ordinal))
                {
                    return columnIndex;
                }
            }
        }

        return -1;
    }

    private static string? ExtractProjectName(DataTable table, int exclusiveEndRow = 10)
    {
        var rowLimit = Math.Min(Math.Max(exclusiveEndRow, 0), table.Rows.Count);
        for (var rowIndex = 0; rowIndex < rowLimit; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.ItemArray.Length; columnIndex++)
            {
                var text = CellText(row[columnIndex]).Trim();
                var separator = text.IndexOfAny(['：', ':']);
                if (separator >= 0)
                {
                    if (TextNormalizer.Normalize(text[..separator]) != "工程名称")
                    {
                        continue;
                    }

                    var inlineValue = text[(separator + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(inlineValue))
                    {
                        return inlineValue;
                    }

                    var adjacentValue = CellText(Cell(row, columnIndex + 1)).Trim();
                    if (!string.IsNullOrWhiteSpace(adjacentValue))
                    {
                        return adjacentValue;
                    }

                    continue;
                }

                if (TextNormalizer.Normalize(text) == "工程名称")
                {
                    var adjacentValue = CellText(Cell(row, columnIndex + 1)).Trim();
                    if (!string.IsNullOrWhiteSpace(adjacentValue))
                    {
                        return adjacentValue;
                    }
                }
            }
        }

        return null;
    }

    private static int FindRow(DataTable table, Func<object?[], bool> predicate)
    {
        for (var index = 0; index < table.Rows.Count; index++)
        {
            if (predicate(table.Rows[index].ItemArray))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindColumn(DataRow row, string expected)
    {
        var normalizedExpected = TextNormalizer.Normalize(expected);
        for (var index = 0; index < row.ItemArray.Length; index++)
        {
            if (TextNormalizer.Normalize(CellText(row[index])).Contains(normalizedExpected, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSummaryNameHeader(string value) =>
        value.Contains("单位工程名称", StringComparison.Ordinal) || value.Contains("单项工程名称", StringComparison.Ordinal);

    private static string FirstRowsText(DataTable table, int rowCount) => string.Join('|', table.Rows.Cast<DataRow>()
        .Take(rowCount)
        .SelectMany(row => row.ItemArray)
        .Select(CellText));

    private static object? Cell(DataRow row, int column) => column >= 0 && column < row.ItemArray.Length ? row[column] : null;

    private static string CellText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string text => text,
        double number => number.ToString("0.################", CultureInfo.InvariantCulture),
        float number => number.ToString("0.################", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static decimal DecimalOrZero(object? value) => TryDecimal(value, out var result) ? result : 0m;

    private static bool TryDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                result = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            default:
                return decimal.TryParse(CellText(value).Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out result)
                    || decimal.TryParse(CellText(value), NumberStyles.Any, CultureInfo.GetCultureInfo("zh-CN"), out result);
        }
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UnitProjectKind DetectKind(string name) => name.Contains("新组价", StringComparison.Ordinal)
        ? UnitProjectKind.NewPrice
        : UnitProjectKind.ContractPrice;

    private static string RoleName(WorkbookRole role) => role switch
    {
        WorkbookRole.Submission => "送审",
        WorkbookRole.InitialReview => "初审",
        WorkbookRole.FinalReview => "终审",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private sealed record SectionFrame(string Title, int Level, int SourceRowIndex)
    {
        public List<int> DirectItemSourceRows { get; } = [];
    }
}
