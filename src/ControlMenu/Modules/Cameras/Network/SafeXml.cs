using System.Xml;
using System.Xml.Linq;

namespace ControlMenu.Modules.Cameras.Network;

/// <summary>
/// Parses untrusted XML (camera / device responses) with hardened reader settings: DTDs
/// prohibited (no entity expansion / no XXE), no external resolver, and a size cap.
/// <para>
/// This is a real hardening, not just style. <see cref="XDocument.Parse(string)"/> does NOT
/// prohibit DTDs by default — that guidance applies to <see cref="XmlReader.Create(System.IO.TextReader)"/>,
/// not to <c>Parse</c>/<c>Load(string)</c>, which allow the DTD and EXPAND internal entities
/// (verified on .NET 10). That is a bounded entity-expansion DoS vector reachable from any device
/// answering camera discovery. (External entities are not fetched by default, so file-exfil XXE was
/// not reachable.) Routing every camera-response parse through this reader prohibits the DTD
/// entirely, drops any external resolver, and caps the document size.
/// </para>
/// </summary>
internal static class SafeXml
{
    // Cap on a single parsed response document. Camera / ISAPI / WS-Discovery responses are small;
    // this stops a hostile or runaway body from being materialised.
    private const int MaxXmlChars = 1_048_576; // ~1 MB

    public static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, // no DTD => no external-entity / XXE expansion
            XmlResolver = null,                     // never resolve external resources
            MaxCharactersInDocument = MaxXmlChars,  // size cap
        };
        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }
}
