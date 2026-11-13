using System.IO.Compression;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace NwcIO.Tests;

internal static class BcfSchemaAssert
{
    internal static void ValidArchive(ZipArchive archive, int topicCount)
    {
        var errors = new List<string>();
        foreach (var (entryName, schemaName) in new[] { ("markup.bcf", "markup.xsd"), ("viewpoint.bcfv", "visinfo.xsd") })
        {
            using var schemaStream = typeof(BcfSchemaAssert).Assembly.GetManifestResourceStream($"NwcIO.Tests.Schemas.Bcf21.{schemaName}");
            Assert.NotNull(schemaStream);
            using var schemaReader = XmlReader.Create(schemaStream);
            var schemas = new XmlSchemaSet();
            schemas.Add(null, schemaReader);
            schemas.Compile();

            var entries = archive.Entries.Where(e => e.Name == entryName).ToList();
            Assert.Equal(topicCount, entries.Count);
            foreach (var entry in entries)
            {
                var settings = new XmlReaderSettings
                {
                    ValidationType = ValidationType.Schema,
                    Schemas = schemas,
                    ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
                };
                settings.ValidationEventHandler += (_, e) => errors.Add($"{entry.FullName}: {e.Message}");
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, settings);
                while (reader.Read())
                {
                }
            }
        }

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
