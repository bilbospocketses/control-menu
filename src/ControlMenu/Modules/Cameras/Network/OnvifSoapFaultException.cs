namespace ControlMenu.Modules.Cameras.Network;

public sealed class OnvifSoapFaultException : Exception
{
    public string? FaultCode { get; }
    public OnvifSoapFaultException(string message, string? faultCode = null) : base(message)
        => FaultCode = faultCode;
}
