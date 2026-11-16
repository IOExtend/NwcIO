namespace NwcIO.Model.TimeLiner;

/// <summary>Date arithmetic that tolerates the extreme values accepted from untrusted schedule files.</summary>
internal static class NwcTimeLinerDates
{
    public static DateTimeOffset AddDays(DateTimeOffset value, int days)
    {
        if (days > 0 && value > DateTimeOffset.MaxValue.AddDays(-days))
        {
            return DateTimeOffset.MaxValue;
        }

        if (days < 0 && value < DateTimeOffset.MinValue.AddDays(-days))
        {
            return DateTimeOffset.MinValue;
        }

        return value.AddDays(days);
    }
}
