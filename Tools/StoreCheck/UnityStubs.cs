// Compile-time stand-ins for the three Unity platform APIs used by ProjectStore.
// They deliberately cannot run. This check binds storage and document APIs;
// it does not claim to validate Unity's platform implementation.
namespace UnityEngine;

public static class Application
{
    public static string persistentDataPath => throw new System.NotSupportedException();
}
public static class Debug
{
    public static void LogException(System.Exception error) => throw new System.NotSupportedException();
}
public static class PlayerPrefs
{
    public static string GetString(string key, string fallback) => throw new System.NotSupportedException();
    public static void SetString(string key, string value) => throw new System.NotSupportedException();
    public static void Save() => throw new System.NotSupportedException();
}
