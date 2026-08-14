namespace FrameHub.Companion.Models;

public sealed record LanCandidateIp(string IpAddress, string InterfaceName, string Description)
{
    public override string ToString() => IpAddress;
}
