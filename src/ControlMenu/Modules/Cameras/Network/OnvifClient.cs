using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Modules.Cameras.Network;

public class OnvifClient : IOnvifClient
{
    private static readonly XNamespace SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace WsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private static readonly XNamespace TdsNs = "http://www.onvif.org/ver10/device/wsdl";
    private static readonly XNamespace TrtNs = "http://www.onvif.org/ver10/media/wsdl";
    private static readonly XNamespace TtNs  = "http://www.onvif.org/ver10/schema";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OnvifClient> _logger;

    public OnvifClient(IHttpClientFactory httpFactory, ILogger<OnvifClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<OnvifDeviceInfo> GetDeviceInformationAsync(string serviceUrl, string username, string password, CancellationToken ct)
    {
        var envelope = BuildEnvelope(username, password, new XElement(TdsNs + "GetDeviceInformation"));
        var doc = await PostAsync(serviceUrl, envelope, ct);
        var resp = doc.Descendants(TdsNs + "GetDeviceInformationResponse").FirstOrDefault()
            ?? throw new OnvifSoapFaultException("GetDeviceInformationResponse missing");
        return new OnvifDeviceInfo(
            resp.Element(TdsNs + "Manufacturer")?.Value ?? "",
            resp.Element(TdsNs + "Model")?.Value ?? "",
            resp.Element(TdsNs + "FirmwareVersion")?.Value ?? "",
            resp.Element(TdsNs + "SerialNumber")?.Value ?? "",
            resp.Element(TdsNs + "HardwareId")?.Value ?? "");
    }

    public async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(string serviceUrl, string username, string password, CancellationToken ct)
    {
        var envelope = BuildEnvelope(username, password, new XElement(TrtNs + "GetProfiles"));
        var doc = await PostAsync(serviceUrl, envelope, ct);
        return doc.Descendants(TrtNs + "Profiles")
            .Select(p => new OnvifProfile(
                p.Attribute("token")?.Value ?? "",
                p.Element(TtNs + "Name")?.Value ?? ""))
            .ToList();
    }

    public async Task<string> GetStreamUriAsync(string serviceUrl, string profileToken, string username, string password, CancellationToken ct)
    {
        var body = new XElement(TrtNs + "GetStreamUri",
            new XElement(TrtNs + "StreamSetup",
                new XElement(TtNs + "Stream", "RTP-Unicast"),
                new XElement(TtNs + "Transport",
                    new XElement(TtNs + "Protocol", "RTSP"))),
            new XElement(TrtNs + "ProfileToken", profileToken));
        var envelope = BuildEnvelope(username, password, body);
        var doc = await PostAsync(serviceUrl, envelope, ct);
        return doc.Descendants(TtNs + "Uri").FirstOrDefault()?.Value
            ?? throw new OnvifSoapFaultException("Stream URI missing in response");
    }

    private static XDocument BuildEnvelope(string username, string password, XElement body)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(nonceBytes);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var digestBytes = SHA1.HashData(nonceBytes
            .Concat(Encoding.UTF8.GetBytes(created))
            .Concat(Encoding.UTF8.GetBytes(password)).ToArray());
        var digest = Convert.ToBase64String(digestBytes);

        return new XDocument(
            new XElement(SoapNs + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", SoapNs),
                new XAttribute(XNamespace.Xmlns + "wsse", WsseNs),
                new XAttribute(XNamespace.Xmlns + "wsu", WsuNs),
                new XAttribute(XNamespace.Xmlns + "tds", TdsNs),
                new XAttribute(XNamespace.Xmlns + "trt", TrtNs),
                new XAttribute(XNamespace.Xmlns + "tt",  TtNs),
                new XElement(SoapNs + "Header",
                    new XElement(WsseNs + "Security",
                        new XElement(WsseNs + "UsernameToken",
                            new XElement(WsseNs + "Username", username),
                            new XElement(WsseNs + "Password",
                                new XAttribute("Type", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"),
                                digest),
                            new XElement(WsseNs + "Nonce",
                                new XAttribute("EncodingType", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                                nonce),
                            new XElement(WsuNs + "Created", created)))),
                new XElement(SoapNs + "Body", body)));
    }

    private async Task<XDocument> PostAsync(string serviceUrl, XDocument envelope, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/soap+xml");
        using var response = await http.PostAsync(serviceUrl, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new OnvifAuthenticationException($"HTTP 401 from {serviceUrl}");

        XDocument doc;
        try { doc = SafeXml.Parse(body); }
        catch (Exception ex) { throw new OnvifSoapFaultException($"Invalid SOAP response: {ex.Message}"); }

        var fault = doc.Descendants(SoapNs + "Fault").FirstOrDefault();
        if (fault is not null)
        {
            var subcode = fault.Descendants(SoapNs + "Subcode")
                .Descendants(SoapNs + "Value").FirstOrDefault()?.Value ?? "";
            if (subcode.Contains("FailedAuthentication", StringComparison.OrdinalIgnoreCase) ||
                subcode.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase))
                throw new OnvifAuthenticationException($"ONVIF auth failed: {subcode}");
            var reason = fault.Descendants(SoapNs + "Text").FirstOrDefault()?.Value ?? subcode;
            throw new OnvifSoapFaultException(reason, subcode);
        }

        return doc;
    }
}
