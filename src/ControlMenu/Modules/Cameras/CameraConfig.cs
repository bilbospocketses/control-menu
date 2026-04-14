namespace ControlMenu.Modules.Cameras;

public record CameraConfig(int Index, string Name, string IpAddress, int Port = 554);
