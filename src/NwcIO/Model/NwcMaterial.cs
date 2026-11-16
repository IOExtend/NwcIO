namespace NwcIO.Model;

/// <summary>
/// A material definition specifying surface colors and PBR properties for 3D meshes.
/// </summary>
public sealed class NwcMaterial
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Base diffuse / albedo RGBA color.</summary>
    public RgbaColor Color { get; set; } = RgbaColor.White;

    /// <summary>PBR metallic factor [0.0 .. 1.0].</summary>
    public float Metallic { get; set; } = 0.1f;

    /// <summary>PBR roughness factor [0.0 .. 1.0].</summary>
    public float Roughness { get; set; } = 0.7f;

    /// <summary>Whether geometry with this material should be rendered double-sided.</summary>
    public bool DoubleSided { get; set; } = true;

    /// <summary>Optional category or classification name.</summary>
    public string? Category { get; set; }

    /// <summary>True when the colour is a neutral fallback, not read from the file (a material named in the properties without a colour record, or the fallback given to meshes without one).</summary>
    public bool IsInferred { get; set; }

    public override string ToString() => $"{Name} ({Color.ToHex()})";
}
