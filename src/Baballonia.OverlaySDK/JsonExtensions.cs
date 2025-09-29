using System.Text.Json;

namespace OverlaySDK;

public static class JsonExtensions
{
    public static bool TryDeserialize<T>(this JsonDocument document, out T? parsed)
    {
        try
        {
            var des = document.Deserialize<T>();
            parsed = des;
            return true;
        }
        catch(Exception ex)
        {
            parsed = default;
            return false;
        }
    }

}
