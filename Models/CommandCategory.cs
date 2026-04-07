namespace SshClient.Models;

public class CommandTip
{
    public string Command { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CommandCategory
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<CommandTip> Tips { get; set; } = [];
}
