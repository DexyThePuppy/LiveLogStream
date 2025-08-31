using System;
using System.Text.RegularExpressions;
using System.Linq;
using ResoniteModLoader;

namespace LiveLogStream;

public static class LogFormatter
{
    private static ModConfiguration config;
    private static ModConfigurationKey<string> timestampColorConfig;
    private static ModConfigurationKey<string> debugColorConfig;
    private static ModConfigurationKey<string> debugTextColorConfig;
    private static ModConfigurationKey<string> infoColorConfig;
    private static ModConfigurationKey<string> infoTextColorConfig;
    private static ModConfigurationKey<string> warningColorConfig;
    private static ModConfigurationKey<string> warningTextColorConfig;
    private static ModConfigurationKey<string> errorColorConfig;
    private static ModConfigurationKey<string> errorTextColorConfig;
    private static ModConfigurationKey<string> stackTraceAtColorConfig;
    private static ModConfigurationKey<string> stackTraceMethodColorConfig;
    private static ModConfigurationKey<string> stackTraceTypeColorConfig;
    private static ModConfigurationKey<string> fpsColorConfig;
    private static ModConfigurationKey<string> elementIdColorConfig;
    private static ModConfigurationKey<string> elementTypeColorConfig;
    private static ModConfigurationKey<string> elementPropertyColorConfig;
    private static ModConfigurationKey<string> elementValueColorConfig;

    public static void Initialize(ModConfiguration configuration, 
        ModConfigurationKey<string> timestampColor,
        ModConfigurationKey<string> debugColor,
        ModConfigurationKey<string> debugTextColor,
        ModConfigurationKey<string> infoColor,
        ModConfigurationKey<string> infoTextColor,
        ModConfigurationKey<string> warningColor,
        ModConfigurationKey<string> warningTextColor,
        ModConfigurationKey<string> errorColor,
        ModConfigurationKey<string> errorTextColor,
        ModConfigurationKey<string> stackTraceAtColor,
        ModConfigurationKey<string> stackTraceMethodColor,
        ModConfigurationKey<string> stackTraceTypeColor,
        ModConfigurationKey<string> fpsColor,
        ModConfigurationKey<string> elementIdColor,
        ModConfigurationKey<string> elementTypeColor,
        ModConfigurationKey<string> elementPropertyColor,
        ModConfigurationKey<string> elementValueColor)
    {
        config = configuration;
        timestampColorConfig = timestampColor;
        debugColorConfig = debugColor;
        debugTextColorConfig = debugTextColor;
        infoColorConfig = infoColor;
        infoTextColorConfig = infoTextColor;
        warningColorConfig = warningColor;
        warningTextColorConfig = warningTextColor;
        errorColorConfig = errorColor;
        errorTextColorConfig = errorTextColor;
        stackTraceAtColorConfig = stackTraceAtColor;
        stackTraceMethodColorConfig = stackTraceMethodColor;
        stackTraceTypeColorConfig = stackTraceTypeColor;
        fpsColorConfig = fpsColor;
        elementIdColorConfig = elementIdColor;
        elementTypeColorConfig = elementTypeColor;
        elementPropertyColorConfig = elementPropertyColor;
        elementValueColorConfig = elementValueColor;
    }

    public static string ProcessRTFTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Keep track of color tags to preserve them
        var colorTagPattern = @"<color[^>]*>.*?</color>";
        var colorTags = Regex.Matches(input, colorTagPattern)
            .Cast<Match>()
            .Select(m => m.Value)
            .ToList();

        // Replace color tags with placeholders
        for (int i = 0; i < colorTags.Count; i++)
        {
            input = input.Replace(colorTags[i], $"__COLOR_TAG_{i}__");
        }

        // Remove all other tags using a general pattern
        input = Regex.Replace(input, @"<[^>]+>", "");

        // Restore color tags
        for (int i = 0; i < colorTags.Count; i++)
        {
            input = input.Replace($"__COLOR_TAG_{i}__", colorTags[i]);
        }

        return input;
    }

    public static string FormatElementDescription(string text)
    {
        // Format Element: ID123, Type: Something.Type descriptions
        text = Regex.Replace(text, 
            @"Element:\s*(ID[\w\d]+|IDCF[\w\d]+)",
            m => $"Element: <color={GetElementIdColor()}>{m.Groups[1].Value}</color>");

        // Format Type: Namespace.Type
        text = Regex.Replace(text,
            @"Type:\s*([\w\.]+)",
            m => $"Type: <color={GetElementTypeColor()}>{m.Groups[1].Value}</color>");

        // Format property names and their values - More precise regex for log format
        text = Regex.Replace(text,
            @"(?<=^|\s|,)([A-Z][\w\s]*?):\s*([^,\n]+?)(?=[,\n]|$)",
            m => $"<color={GetElementPropertyColor()}>{m.Groups[1].Value.TrimEnd()}:</color> <color={GetElementValueColor()}>{m.Groups[2].Value.Trim()}</color>");
        
        return text;
    }

    public static string FormatTimestamp(string text)
    {
        return Regex.Replace(text, 
            @"^(\d{2}:\d{2}:\d{2}(?:\.\d{3})?)",
            m => $"<b><color={GetTimestampColor()}>[{m.Groups[1].Value}]</color></b>");
    }

    public static string FormatFPS(string text)
    {
        return Regex.Replace(text, 
            @"\((\s*\d+\s*FPS\s*)\)", 
            m => $"<b><color={GetFpsColor()}>[{m.Groups[1].Value}]</color></b>");
    }

    public static string FormatBrackets(string text)
    {
        return Regex.Replace(text, @"\[(.*?)\]", "<b>[$1]</b>");
    }

    public static string ApplyLogLevelColor(string text, string level)
    {
        return level switch
        {
            "DEBUG" => $"<color={GetDebugTextColor()}>{text}</color>",
            "INFO" => $"<color={GetInfoTextColor()}>{text}</color>",
            "WARNING" => $"<color={GetWarningTextColor()}>{text}</color>",
            "ERROR" => $"<color={GetErrorTextColor()}>{text}</color>",
            _ => text
        };
    }

    public static string FormatLogLevel(string level)
    {
        return level switch
        {
            "DEBUG" => $"<color={GetDebugColor()}><b>[DEBUG]</b></color>",
            "INFO" => $"<color={GetInfoColor()}><b>[INFO]</b></color>",
            "WARNING" => $"<color={GetWarningColor()}><b>[WARNING]</b></color>",
            "ERROR" => $"<color={GetErrorColor()}><b>[ERROR]</b></color>",
            _ => $"<color={GetTimestampColor()}><b>[{level}]</b></color>"
        };
    }

    public static string FormatStackTrace(string text)
    {
        // Format 'at' in stack traces
        text = Regex.Replace(text, @"\bat\b", m => $"<color={GetStackTraceAtColor()}>at</color>");
        
        // Format method names in stack traces
        text = Regex.Replace(text, @"([A-Za-z_][A-Za-z0-9_]*)\(", m => $"<color={GetStackTraceMethodColor()}>{m.Groups[1].Value}</color>(");
        
        // Format type names in stack traces
        text = Regex.Replace(text, @"([A-Z][A-Za-z0-9_]*\.[A-Za-z0-9_]+)", m => $"<color={GetStackTraceTypeColor()}>{m.Groups[1].Value}</color>");
        
        return text;
    }

    public static string StripFormatting(string text)
    {
        // Strip existing formatting more precisely using fully qualified name
        var regex = new Regex(@"</?(?:color|b|i|size|align)(?:\s+[^>]*)?>");
        return regex.Replace(text, string.Empty);
    }

    // Color getter methods
    private static string GetTimestampColor() => config?.GetValue(timestampColorConfig) ?? "#B5B5B5";
    private static string GetDebugColor() => config?.GetValue(debugColorConfig) ?? "#C5A3FF";
    private static string GetDebugTextColor() => config?.GetValue(debugTextColorConfig) ?? "#A18DBF";
    private static string GetInfoColor() => config?.GetValue(infoColorConfig) ?? "#A8E6CF";
    private static string GetInfoTextColor() => config?.GetValue(infoTextColorConfig) ?? "#8FC5AA";
    private static string GetWarningColor() => config?.GetValue(warningColorConfig) ?? "#FFD3B6";
    private static string GetWarningTextColor() => config?.GetValue(warningTextColorConfig) ?? "#E6B89C";
    private static string GetErrorColor() => config?.GetValue(errorColorConfig) ?? "#FFAAA5";
    private static string GetErrorTextColor() => config?.GetValue(errorTextColorConfig) ?? "#E69B95";
    private static string GetStackTraceAtColor() => config?.GetValue(stackTraceAtColorConfig) ?? "#B5B5B5";
    private static string GetStackTraceMethodColor() => config?.GetValue(stackTraceMethodColorConfig) ?? "#B5C1FF";
    private static string GetStackTraceTypeColor() => config?.GetValue(stackTraceTypeColorConfig) ?? "#98D8D8";
    private static string GetFpsColor() => config?.GetValue(fpsColorConfig) ?? "#A4C2F4";
    private static string GetElementIdColor() => config?.GetValue(elementIdColorConfig) ?? "#F8C8DC";
    private static string GetElementTypeColor() => config?.GetValue(elementTypeColorConfig) ?? "#98D8D8";
    private static string GetElementPropertyColor() => config?.GetValue(elementPropertyColorConfig) ?? "#E0BBE4";
    private static string GetElementValueColor() => config?.GetValue(elementValueColorConfig) ?? "#B5E5FF";
}
