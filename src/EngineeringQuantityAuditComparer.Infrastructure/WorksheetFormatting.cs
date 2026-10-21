using ClosedXML.Excel;

namespace EngineeringQuantityAuditComparer.Infrastructure;

internal static class WorksheetFormatting
{
    private static readonly XLColor BaseHeader = XLColor.FromHtml("#EDF8F7");
    private static readonly XLColor SubmissionHeader = XLColor.FromHtml("#E2F0D9");
    private static readonly XLColor ReviewHeader = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor DifferenceHeader = XLColor.FromHtml("#D9EAD3");
    private static readonly XLColor NegativeFill = XLColor.FromHtml("#E2F0D9");
    private static readonly XLColor NegativeFont = XLColor.FromHtml("#006100");
    private static readonly XLColor PositiveFill = XLColor.FromHtml("#F4CCCC");
    private static readonly XLColor PositiveFont = XLColor.FromHtml("#C00000");
    private static readonly XLColor Accent = XLColor.FromHtml("#2F7570");

    public static void ApplyTitle(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Accent;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 16;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 28;
    }

    public static void StyleHeader(IXLRange range, XLColor fill)
    {
        range.Style.Fill.BackgroundColor = fill;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
    }

    public static void StyleSectionTitle(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Accent;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
    }

    public static void StyleSummaryRow(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = BaseHeader;
        range.Style.Font.Bold = true;
    }

    public static void ApplyBorders(IXLRange range)
    {
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
        range.Style.Border.RightBorder = XLBorderStyleValues.Thin;
    }

    public static void AddSignConditionalFormats(IXLRange range)
    {
        var negative = range.AddConditionalFormat().WhenLessThan(0);
        negative.Fill.SetBackgroundColor(NegativeFill);
        negative.Font.SetFontColor(NegativeFont);
        var positive = range.AddConditionalFormat().WhenGreaterThan(0);
        positive.Fill.SetBackgroundColor(PositiveFill);
        positive.Font.SetFontColor(PositiveFont);
    }

    public static void ConfigurePrint(IXLWorksheet worksheet, int lastRow, int lastColumn)
    {
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PagesTall = 0;
        worksheet.PageSetup.Margins.Top = 0.4;
        worksheet.PageSetup.Margins.Bottom = 0.4;
        worksheet.PageSetup.PrintAreas.Clear();
        worksheet.PageSetup.PrintAreas.Add(1, 1, lastRow, lastColumn);
    }

    public static void WriteOptionalText(IXLCell cell, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            cell.Clear(XLClearOptions.Contents);
            return;
        }

        cell.Value = value;
    }

    public static void WriteNumber(IXLCell cell, decimal value) => cell.Value = value;

    public static void SetInternalHyperlink(
        IXLCell cell,
        string displayText,
        string sheetName,
        int row)
    {
        cell.Value = displayText;
        var escapedSheetName = sheetName.Replace("'", "''", StringComparison.Ordinal);
        cell.SetHyperlink(new XLHyperlink($"'{escapedSheetName}'!A{row}")
        {
            Tooltip = $"跳转到 {sheetName}"
        });
        cell.Style.Font.FontColor = XLColor.FromHtml("#0563C1");
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }

    public static void SetRowValues(IXLWorksheet worksheet, int row, int startColumn, IReadOnlyList<object?> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var cell = worksheet.Cell(row, startColumn + index);
            if (values[index] is null || values[index] is string text && string.IsNullOrWhiteSpace(text))
            {
                cell.Clear(XLClearOptions.Contents);
            }
            else
            {
                cell.Value = XLCellValue.FromObject(values[index]);
            }
        }
    }

    public static XLColor HeaderColor(EngineeringQuantityAuditComparer.Core.WorkbookRole stage) => stage switch
    {
        EngineeringQuantityAuditComparer.Core.WorkbookRole.Submission => SubmissionHeader,
        EngineeringQuantityAuditComparer.Core.WorkbookRole.InitialReview or
            EngineeringQuantityAuditComparer.Core.WorkbookRole.FinalReview => ReviewHeader,
        _ => BaseHeader
    };

    public static XLColor BaseHeaderColor => BaseHeader;
    public static XLColor DifferenceHeaderColor => DifferenceHeader;
    public static XLColor PositiveFontColor => PositiveFont;
    public static XLColor NegativeFontColor => NegativeFont;
}
