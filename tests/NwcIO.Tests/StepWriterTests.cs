using NwcIO.Internal.Export;
using Xunit;

namespace NwcIO.Tests;

public class StepWriterTests
{
    [Fact]
    public void StepWriter_StreamsHeaderAndEntitiesBeforeCompletion()
    {
        using var output = new StringWriter();

        var writer = new StepWriter(output, "streamed.ifc");

        Assert.Contains("ISO-10303-21;", output.ToString());
        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", output.ToString());
        Assert.DoesNotContain("END-ISO-10303-21;", output.ToString());

        int first = writer.Add("IFCCARTESIANPOINT", "0.,0.,0.");
        int second = writer.Add("IFCDIRECTION", "(0.,0.,1.)");

        string written = output.ToString();
        Assert.Contains($"#{first}=IFCCARTESIANPOINT(0.,0.,0.);", written);
        Assert.Contains($"#{second}=IFCDIRECTION((0.,0.,1.));", written);

        writer.Complete();
        Assert.Contains("ENDSEC;", output.ToString());
        Assert.Contains("END-ISO-10303-21;", output.ToString());
    }
}
