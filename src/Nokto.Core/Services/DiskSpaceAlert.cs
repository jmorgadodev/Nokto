namespace Nokto.Core.Services;

public static class DiskSpaceAlert
{
    public static bool IsLowSpace(long? freeGigabytes, int thresholdGigabytes) =>
        freeGigabytes is >= 0 && thresholdGigabytes >= 0 && freeGigabytes <= thresholdGigabytes;
}
