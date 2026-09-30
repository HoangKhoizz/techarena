namespace ban_link_kien_PC.Domain.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "PcStore";
    public string Audience { get; set; } = "PcStore.Staff";
    public string Key { get; set; } = "PcStore-Dev-Jwt-Signing-Key-Change-Me-32+";
    public int ExpireMinutes { get; set; } = 480;
}
