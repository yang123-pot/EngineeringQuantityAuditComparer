namespace EngineeringQuantityAuditComparer.Tests;

public sealed class MainWindowBindingTests
{
    [Fact]
    public void Unit_match_grid_uses_full_rows_and_all_manual_move_commands()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SourceUnderTest", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("ItemsSource=\"{Binding UnitMatchRows}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedUnitMatchRow", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveInitialUpCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveInitialDownCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveFinalUpCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveFinalDownCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsConfirmed", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsBlocking}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding UnitExceptions}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_review_screen_omits_item_exception_controls()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SourceUnderTest", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.DoesNotContain("清单项目匹配异常", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding ItemExceptions}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialCandidates", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalCandidates", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"确认单位工程并进入预览\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_uses_only_submission_based_difference_columns_with_dynamic_color_styles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SourceUnderTest", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("Header=\"差异（送审-初审）\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"差异（送审-终审）\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"送审-初审\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"送审-终审\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitFinalVsInitialColumn", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemFinalVsInitialColumn", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalVsInitialDifference", xaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(xaml, "CellStyle=\"{StaticResource InitialDifferenceCellStyle}\""));
        Assert.Equal(2, CountOccurrences(xaml, "CellStyle=\"{StaticResource FinalDifferenceCellStyle}\""));
    }

    [Theory]
    [InlineData(1, "#FFC00000")]
    [InlineData(-1, "#FF006100")]
    public void Difference_color_converter_maps_positive_to_red_and_negative_to_green(int value, string expected)
    {
        var converter = new App.DifferenceToBrushConverter();

        var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(
            converter.Convert(value, typeof(System.Windows.Media.Brush), null!, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, brush.Color.ToString());
    }

    [Fact]
    public void Difference_color_converter_leaves_zero_neutral()
    {
        var converter = new App.DifferenceToBrushConverter();

        var result = converter.Convert(0m, typeof(System.Windows.Media.Brush), null!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Same(System.Windows.DependencyProperty.UnsetValue, result);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}
