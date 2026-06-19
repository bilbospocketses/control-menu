using System.Xml;
using System.Xml.Linq;

namespace ControlMenu.Modules.Cameras.Network;

/// <summary>
/// Parses untrusted XML (camera / device responses) with hardened reader settings: DTDs
/// prohibited (no external-entity / XXE), no external resolver, and a size cap. Defense-in-depth —
/// <see cref="XDocument.Parse(string)"/> already prohibits DTDs by default on this runtime, but
/// routing every camera-response parse through one explicit safe reader keeps the guarantee local
/// and future-proof.
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
