using NwcIO.Model;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public sealed class ClashCancellationTests
{
    [Fact]
    public void Run_WithCanceledToken_ThrowsOperationCanceled()
    {
        var document = new NwcDocumentBuilder()
            .AddMesh(new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            })
            .Build();
        var test = new NwcClashTest("Cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NwcClashDetector.Run(document, test, null, cancellation.Token));
    }

    [Fact]
    public void Run_ReportsProgressToCompletion()
    {
        var document = new NwcDocumentBuilder()
            .AddMesh(new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            })
            .AddMesh(new NwcMesh
            {
                Id = 2,
                Vertices = [new(2, 0, 0), new(3, 0, 0), new(2, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(2, 0, 0), new(3, 1, 0)),
            })
            .Build();
        var test = new NwcClashTest("Progress");
        var progress = new CollectingProgress();

        NwcClashDetector.Run(document, test, progress, CancellationToken.None);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal("clash", progress.Reports[^1].Phase);
        Assert.Equal(2, progress.Reports[^1].Completed);
        Assert.Equal(2, progress.Reports[^1].Total);
    }

    [Fact]
    public void Run_ProgressCanCancelAfterTheFirstMesh()
    {
        var document = new NwcDocumentBuilder()
            .AddMesh(Triangle(1, 0))
            .AddMesh(Triangle(2, 2))
            .AddMesh(Triangle(3, 4))
            .Build();
        var test = new NwcClashTest("Cancel mid-run");
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);

        Assert.Throws<OperationCanceledException>(() =>
            NwcClashDetector.Run(document, test, progress, cancellation.Token));
        Assert.Single(progress.Reports);
    }

    [Fact]
    public void DocumentAndAssemblyRunClashDetection_AcceptCancellation()
    {
        var document = NwcDocument.Create(meshes: [Triangle(1, 0), Triangle(2, 2)]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            document.RunClashDetection(null, null, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() =>
            NwcAssembly.Create("fed", document).RunClashDetection("t", 0.001, null, cancellation.Token));
    }

    private static NwcMesh Triangle(int id, double x) => new()
    {
        Id = id,
        Vertices = [new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0)],
        Indices = [0, 1, 2],
        BoundingBox = new BoundingBox3d(new(x, 0, 0), new(x + 1, 1, 0)),
    };

    private sealed class CollectingProgress : IProgress<NwcProgress>
    {
        public List<NwcProgress> Reports { get; } = [];

        public void Report(NwcProgress value) => Reports.Add(value);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<NwcProgress>
    {
        public List<NwcProgress> Reports { get; } = [];

        public void Report(NwcProgress value)
        {
            Reports.Add(value);
            cancellation.Cancel();
        }
    }
}
