namespace FanForge.Core;

/// <summary>
/// Decodes the status-tagged values returned by the ASUS ACPI/WMI fan channels.
/// </summary>
/// <remarks>
/// ASUS returns a 32-bit value whose bits above 16 form a status/validity flag and whose low 16 bits
/// are the payload. Empirically (hardware probe matrix and independent research) a valid reading has
/// bit <c>0x10000</c> set and the payload is the low 16 bits; sentinels such as <c>0xFFFFFFFE</c>
/// clear that bit. Fan payloads are in units of 100 RPM.
/// </remarks>
public static class AsusWmiCodec
{
    public const uint ValidFlag = 0x00010000;
    public const uint PayloadMask = 0x0000FFFF;

    /// <summary>Extracts the low-16 payload. Returns false when the validity bit is clear.</summary>
    public static bool TryDecodeValue(uint raw, out int value)
    {
        value = 0;
        if ((raw & ValidFlag) == 0)
        {
            return false;
        }

        value = (int)((raw - ValidFlag) & PayloadMask);
        return true;
    }

    /// <summary>
    /// Decodes a fan reading to RPM. Returns null for invalid sentinels or implausible payloads
    /// (&gt; <see cref="FanPolicy.MaxAcpiFanRaw"/>) so a bad register can never be shown as RPM.
    /// </summary>
    public static int? DecodeFanRpm(uint raw)
    {
        if (!TryDecodeValue(raw, out int value))
        {
            return null;
        }

        if (value > FanPolicy.MaxAcpiFanRaw)
        {
            return null;
        }

        return value * 100;
    }
}
