namespace NwcIO.Model;

/// <summary>Severity of a reader diagnostic.</summary>
public enum NwcDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// Describes data that a reader skipped or substituted, such as an unresolved
/// external reference or an unrecognized container tail.
/// </summary>
/// <param name="Severity">Whether the loss makes the result incomplete or merely suspicious.</param>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="StreamIndex">Index of the affected container stream, when known.</param>
/// <param name="ByteOffset">Byte offset in the affected stream or file, when known.</param>
public sealed record NwcDiagnostic(
    NwcDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? StreamIndex = null,
    long? ByteOffset = null);
