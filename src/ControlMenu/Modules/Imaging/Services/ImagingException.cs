namespace ControlMenu.Modules.Imaging.Services;

public class ImagingException : Exception
{
    public ImagingException(string message) : base(message) { }
    public ImagingException(string message, Exception inner) : base(message, inner) { }
}
