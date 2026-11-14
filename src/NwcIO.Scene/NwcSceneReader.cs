using System.Globalization;
using System.Reflection;
using IOScene;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;
using BoundingBox3d = IOScene.BoundingBox3d;

namespace NwcIO.Scene;

/// <summary>Converts a Navisworks model file, or an <see cref="NwcDocument"/> read by NwcIO, into a <see cref="SceneDocument"/>.</summary>
/// <remarks>
/// The scene has one <see cref="SceneModel"/>: <c>Key</c> and <c>Name</c> are <see cref="NwcSceneOptions.ModelKey"/>, else the file
/// name; <c>SourceFormat</c> is <c>nwc</c>; <c>Application</c> is the loader plugin after its last <c>:</c> (for example
/// <c>lcldrevit</c>); <c>CreatedAt</c> is the source timestamp in UTC; <c>SourceLengthUnit</c> is <c>ft</c> when the unit used is feet.
/// </remarks>
public static class NwcSceneReader
{
    private const int ModelId = 1;

    private static readonly string Producer = "NwcIO.Scene "
        + typeof(NwcSceneReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    /// <summary>Reads the model file at <paramref name="path"/> with <see cref="NwcDocument.Open(string, NwcReaderOptions?)"/> and converts it.</summary>
    /// <param name="path">The model file.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <see cref="NwcSceneOptions.ModelKey"/> is empty.</exception>
    /// <exception cref="NotSupportedException">The file's scene tree holds <c>.nwd</c> partition nodes or a Revit link.</exception>
    public static NwcSceneResult Read(string path, NwcSceneOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string key = options?.ModelKey ?? Path.GetFileName(path);
        if (key.Length == 0)
        {
            throw new ArgumentException("NwcSceneOptions.ModelKey must not be empty; leave it null to use the file name.", nameof(options));
        }

        NwcDocument document;
        try
        {
            document = NwcDocument.Open(path);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException("NwcIO.Scene needs the scene tree: .nwd partition trees and Revit links are not supported yet. " + ex.Message, ex);
        }

        return Convert(document, key, path, options);
    }

    /// <summary>
    /// Converts <paramref name="document"/>, which has no file name, so <see cref="NwcSceneOptions.ModelKey"/> is required.
    /// Give a document opened from a model file with the default <see cref="NwcReaderOptions"/>. With
    /// <see cref="NwcReaderOptions.ReadPropertyRecords"/>, <see cref="NwcReaderOptions.ReadMeshes"/> or
    /// <see cref="NwcReaderOptions.ReadMaterials"/> off the scene lacks property sets and levels, geometry, or materials, without a
    /// diagnostic (the document does not record its options); a document not read through the file's DbCache directory has meshes
    /// without tree nodes and properties outside tabs, both reported and left out.
    /// </summary>
    /// <param name="document">The document, for example from <see cref="NwcDocument.Open(Stream, NwcReaderOptions?, string?)"/>.</param>
    /// <param name="options">Conversion options with <see cref="NwcSceneOptions.ModelKey"/>.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="NwcSceneOptions.ModelKey"/> is null or empty, or the document has no scene tree (<see cref="NwcDocument.RootNode"/> is null).
    /// </exception>
    public static NwcSceneResult Read(NwcDocument document, NwcSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ModelKey))
        {
            throw new ArgumentException("A document converted from memory needs NwcSceneOptions.ModelKey.", nameof(options));
        }

        if (document.RootNode is null)
        {
            throw new ArgumentException("The document has no scene tree: open it with NwcReaderOptions.BuildSceneTree on.", nameof(document));
        }

        return Convert(document, options.ModelKey, null, options);
    }

    private static NwcSceneResult Convert(NwcDocument document, string key, string? path, NwcSceneOptions? options)
    {
        var unit = options?.LengthUnit ?? document.LengthUnit;
        string? loader = document.FileInfo.LoaderPlugin;
        var scene = new SceneDocument { Title = key };
        scene.Models.Add(new SceneModel
        {
            Id = ModelId,
            Key = key,
            Name = key,
            Path = path,
            SourceFormat = "nwc",
            Producer = Producer,
            SourceLengthUnit = unit == NwcLengthUnit.Feet ? "ft" : null,
            Application = loader?[(loader.LastIndexOf(':') + 1)..],
            CreatedAt = document.FileInfo.SourceTimestampUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });

        return new NwcSceneResult
        {
            Document = scene,
            LengthUnit = unit,
            MetresPerUnit = unit == NwcLengthUnit.Feet ? 0.3048 : null,
        };
    }
}
