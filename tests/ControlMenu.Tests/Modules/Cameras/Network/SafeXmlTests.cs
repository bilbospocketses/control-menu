using System.Xml;
using ControlMenu.Modules.Cameras.Network;

namespace ControlMenu.Tests.Modules.Cameras.Network;

public class SafeXmlTests
{
    [Fact]
    public void Parse_ValidXml_ReturnsDocument()
    {
        var doc = SafeXml.Parse("<root><child>hello</child></root>");
        Assert.Equal("root", doc.Root!.Name.LocalName);
        Assert.Equal("hello", doc.Root!.Element("child")!.Value);
    }

    [Fact]
    public void Parse_OversizedXml_Throws()
    {
        var huge = "<root>" + new string('a', 1_100_000) + "</root>"; // > 1 MB
        Assert.Throws<XmlException>(() => SafeXml.Parse(huge));
    }

    [Fact]
    public void Parse_WithInternalDtd_Throws()
    {
        var dtd = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY x \"y\">]><root>&x;</root>";
        Assert.Throws<XmlException>(() => SafeXml.Parse(dtd));
    }

    [Fact]
    public void Parse_WithExternalEntity_Throws_AndNeverResolves()
    {
        // XXE attempt. DTD processing is prohibited, so parsing throws before the external
        // resource is ever fetched.
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]><root>&xxe;</root>";
        Assert.Throws<XmlException>(() => SafeXml.Parse(xxe));
    }

    [Fact]
    public void RawXDocumentParse_ExpandsInternalEntities_WhichIsWhySafeXmlExists()
    {
        // Characterizes the framework default this hardening actually guards against:
        // XDocument.Parse(string) does NOT prohibit DTDs by default — it allows the DTD and
        // EXPANDS internal entities (the "prohibit by default" guidance applies to
        // XmlReader.Create, not XDocument.Parse/XElement.Parse). That is a real, bounded
        // entity-expansion DoS vector. SafeXml.Parse prohibits the DTD, so the same input throws.
        const string dtd = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY x \"EXPANDED\">]><root>&x;</root>";
        Assert.Equal("EXPANDED", System.Xml.Linq.XDocument.Parse(dtd).Root!.Value); // framework default expands
        Assert.Throws<XmlException>(() => SafeXml.Parse(dtd));                        // SafeXml blocks it
    }
}
