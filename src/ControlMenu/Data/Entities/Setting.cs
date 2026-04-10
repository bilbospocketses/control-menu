namespace ControlMenu.Data.Entities;

public class Setting
{
    public Guid Id { get; set; }
    public string? ModuleId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public bool IsSecret { get; set; }
}
