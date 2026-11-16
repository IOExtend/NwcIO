using System.Text;
using NwcIO.Model.TimeLiner;
using Xunit;

namespace NwcIO.Tests;

public class TimeLinerDateFallbackTests
{
    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Csv_StartAtMaxValueAndMissingEnd_ClampsInsteadOfThrowing()
    {
        const string csv = "h\nT1,A,Construct,9999-12-31,\nT2,B,Construct,9999-12-28,\n";

        var read = NwcTimeLinerCsvSerializer.Read(new StringReader(csv));

        Assert.Equal(2, read.Tasks.Count);
        Assert.Equal(DateTimeOffset.MaxValue, read.Tasks[0].PlannedEnd);
        Assert.Equal(DateTimeOffset.MaxValue, read.Tasks[1].PlannedEnd);
        Assert.Contains(read.Warnings, w => w.Contains("planned end", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MsProjectXml_StartAtMaxValueAndMissingFinish_ClampsInsteadOfThrowing()
    {
        const string xml = """
            <Project xmlns="http://schemas.microsoft.com/project">
              <Tasks>
                <Task><Name>A</Name><WBS>1</WBS><Start>9999-12-31T00:00:00</Start></Task>
              </Tasks>
            </Project>
            """;

        var read = NwcTimeLinerMsProjectXmlSerializer.Read(Utf8(xml));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(DateTimeOffset.MaxValue, task.PlannedEnd);
        Assert.Contains(read.Warnings, w => w.Contains("finish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrimaveraXer_StartAtMaxValueAndMissingEnd_ClampsInsteadOfThrowing()
    {
        const string xer = "%T\tTASK\n%F\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date\n%R\tT1\tA\t9999-12-31\t\n";

        var read = NwcTimeLinerPrimaveraXerSerializer.Read(Utf8(xer));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(DateTimeOffset.MaxValue, task.PlannedEnd);
        Assert.Contains(read.Warnings, w => w.Contains("target end", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Csv_MissingStartWithValidEnd_DerivesStartFromEnd()
    {
        const string csv = "h\nT1,A,Construct,not-a-date,2020-03-08\n";

        var read = NwcTimeLinerCsvSerializer.Read(new StringReader(csv));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero), task.PlannedStart);
        Assert.Equal(new DateTimeOffset(2020, 3, 8, 0, 0, 0, TimeSpan.Zero), task.PlannedEnd);
        Assert.Equal(task.PlannedStart, read.OverallStart);
        Assert.Contains(read.Warnings, w => w.Contains("planned start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Xml_MissingStartWithValidEnd_DerivesStartFromEnd()
    {
        const string xml = """
            <exchange name="S"><tasks><task name="A" id="1"><planned end="2020-03-08T00:00:00+00:00" /></task></tasks></exchange>
            """;

        var read = NwcTimeLinerXmlSerializer.Read(Utf8(xml));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero), task.PlannedStart);
        Assert.Equal(task.PlannedStart, read.OverallStart);
        Assert.Contains(read.Warnings, w => w.Contains("planned start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MsProjectXml_MissingStartWithValidFinish_DerivesStartFromFinish()
    {
        const string xml = """
            <Project xmlns="http://schemas.microsoft.com/project">
              <Tasks>
                <Task><Name>A</Name><WBS>1</WBS><Finish>2020-03-08T00:00:00</Finish></Task>
              </Tasks>
            </Project>
            """;

        var read = NwcTimeLinerMsProjectXmlSerializer.Read(Utf8(xml));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(new DateTimeOffset(2020, 3, 7, 0, 0, 0, TimeSpan.Zero), task.PlannedStart);
        Assert.Equal(task.PlannedStart, read.OverallStart);
        Assert.Contains(read.Warnings, w => w.Contains("start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrimaveraXer_MissingStartWithValidEnd_DerivesStartFromEnd()
    {
        const string xer = "%T\tTASK\n%F\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date\n%R\tT1\tA\t\t2020-03-08\n";

        var read = NwcTimeLinerPrimaveraXerSerializer.Read(Utf8(xer));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(new DateTimeOffset(2020, 3, 7, 0, 0, 0, TimeSpan.Zero), task.PlannedStart);
        Assert.Equal(task.PlannedStart, read.OverallStart);
        Assert.Contains(read.Warnings, w => w.Contains("target start", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("9999-12-31T00:00:00+00:00")]
    [InlineData("9999-12-28T00:00:00+00:00")]
    public void Xml_StartNearMaxValueAndMissingEnd_ClampsToMaxValue(string start)
    {
        var xml = $"""
            <exchange name="S"><tasks><task name="A" id="1"><planned start="{start}" /></task></tasks></exchange>
            """;

        var read = NwcTimeLinerXmlSerializer.Read(Utf8(xml));

        var task = Assert.Single(read.Tasks);
        Assert.Equal(DateTimeOffset.MaxValue, task.PlannedEnd);
    }

    [Fact]
    public void Overall_IgnoresEpochPlaceholderTasks_AndFallsBackToEpochWhenAllArePlaceholders()
    {
        var real = new NwcTimeLinerTask("r", "R", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var placeholder = new NwcTimeLinerTask("p", "P", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(7));

        var mixed = new NwcTimeLinerSchedule("S");
        mixed.Tasks.Add(real);
        mixed.Tasks.Add(placeholder);
        Assert.Equal(real.PlannedStart, mixed.OverallStart);
        Assert.Equal(real.PlannedEnd, mixed.OverallEnd);

        var onlyPlaceholder = new NwcTimeLinerSchedule("S");
        onlyPlaceholder.Tasks.Add(placeholder);
        Assert.Equal(DateTimeOffset.UnixEpoch, onlyPlaceholder.OverallStart);
    }
}
