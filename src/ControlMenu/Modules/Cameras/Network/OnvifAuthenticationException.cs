namespace ControlMenu.Modules.Cameras.Network;

public sealed class OnvifAuthenticationException : Exception
{
    public OnvifAuthenticationException(string message) : base(message) { }
}
