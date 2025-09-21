using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Elements.Core;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Newtonsoft.Json;

namespace LiveLogStream;



public class LiveLogMod : ResoniteMod
{
    public override string Name => "LiveLogStream";
    public override string Author => "Dexy";
    public override string Version => "2.0.0";
    public override string Link => "https://github.com/dexy/LiveLogStream";

    private static string MOD_NAME = "LiveLogStream";
    internal static string HarmonyId => $"com.Dexy.{MOD_NAME}";
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<int> maxLinesConfig = new ModConfigurationKey<int>("maxLines", "Maximum number of log lines to keep in history", () => 500);
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<int> streamUpdatePeriodConfig = new ModConfigurationKey<int>("streamUpdatePeriod", "Update period for ValueStream (0 = every frame, higher = less frequent)", () => 0);
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> timestampColorConfig = new ModConfigurationKey<string>("timestampColor", "Color for timestamps (hex)", () => "#B5B5B5");  // Soft Gray
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> debugColorConfig = new ModConfigurationKey<string>("debugColor", "Color for debug tags (hex)", () => "#C5A3FF");  // Pastel Purple
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> debugTextColorConfig = new ModConfigurationKey<string>("debugTextColor", "Color for debug message text (hex)", () => "#A18DBF");  // Soft Purple

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> infoColorConfig = new ModConfigurationKey<string>("infoColor", "Color for info tags (hex)", () => "#A8E6CF");  // Mint Green
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> infoTextColorConfig = new ModConfigurationKey<string>("infoTextColor", "Color for info message text (hex)", () => "#8FC5AA");  // Soft Green
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> warningColorConfig = new ModConfigurationKey<string>("warningColor", "Color for warning tags (hex)", () => "#FFD3B6");  // Peach
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> warningTextColorConfig = new ModConfigurationKey<string>("warningTextColor", "Color for warning message text (hex)", () => "#E6B89C");  // Soft Peach

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> errorColorConfig = new ModConfigurationKey<string>("errorColor", "Color for error tags (hex)", () => "#FFAAA5");  // Pastel Red
    
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> errorTextColorConfig = new ModConfigurationKey<string>("errorTextColor", "Color for error message text (hex)", () => "#E69B95");  // Soft Red

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> stackTraceAtColorConfig = new ModConfigurationKey<string>("stackTraceAtColor", "Color for 'at' in stack traces (hex)", () => "#B5B5B5");  // Soft Gray

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> stackTraceMethodColorConfig = new ModConfigurationKey<string>("stackTraceMethodColor", "Color for method names in stack traces (hex)", () => "#B5C1FF");  // Pastel Blue

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> stackTraceTypeColorConfig = new ModConfigurationKey<string>("stackTraceTypeColor", "Color for type names in stack traces (hex)", () => "#98D8D8");  // Pastel Turquoise

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> fpsColorConfig = new ModConfigurationKey<string>("fpsColor", "Color for FPS indicators (hex)", () => "#A4C2F4");  // Pastel Blue

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> elementIdColorConfig = new ModConfigurationKey<string>("elementIdColor", "Color for element IDs (hex)", () => "#F8C8DC");  // Pastel Pink

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> elementTypeColorConfig = new ModConfigurationKey<string>("elementTypeColor", "Color for element types (hex)", () => "#98D8D8");  // Pastel Turquoise

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> elementPropertyColorConfig = new ModConfigurationKey<string>("elementPropertyColor", "Color for element properties (hex)", () => "#E0BBE4");  // Pastel Lavender

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<string> elementValueColorConfig = new ModConfigurationKey<string>("elementValueColor", "Color for element values (hex)", () => "#B5E5FF");  // Pastel Sky Blue

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<bool> reloadLogsConfig = new ModConfigurationKey<bool>("reloadLogs", "Toggle to refresh log formatting with current config values", () => false);

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<bool> clearLogsConfig = new ModConfigurationKey<bool>("clearLogs", "Toggle to clear all logs", () => false);
    
    private static ModConfiguration config;
    private static Harmony harmony;
    private static LiveLogMod instance;  // Add static instance reference
    
    // Configuration values
    private static int maxLines = 500;  // Will be updated from config
    private static int streamUpdatePeriod = 0;  // Will be updated from config
    private static HashSet<string> initializedUsers = new();
    private static bool engineInitialized = false;
    private static bool onReadyHandled = false;

    // ValueStream management - following Resonance pattern
    private static ConcurrentDictionary<string, LiveLogStreamHandler> userLogHandlers = new();

    private static Dictionary<ModConfigurationKey<string>, ModConfigurationKey<string>.OnChangedHandler> colorConfigHandlers = new();
    private static ModConfigurationKey<int>.OnChangedHandler maxLinesHandler = (value) => ResoniteMod.Msg($"Max lines changed to: {value}");
    
    // World change detection
    private static World lastKnownWorld = null;
    private static bool isCleaningUp = false;



    private static void UpdateMaxLines(int newMax)
    {
        maxLines = newMax;
        // Update all handlers with new max lines
        foreach (var handler in userLogHandlers.Values)
        {
            handler.UpdateMaxLines(newMax);
        }
    }



    private static void ProcessLog(string text, string level = "")
    {
        if (!engineInitialized || Engine.Current?.WorldManager?.FocusedWorld == null) return;

        // Skip processing if we're in cleanup mode to prevent infinite loops
        if (isCleaningUp) return;

        var world = Engine.Current.WorldManager.FocusedWorld;
        
        // Check for world changes and clean up old handlers if needed
        CheckAndHandleWorldChange(world);
        
        var localUser = world.LocalUser;
        if (localUser == null) return;

        var userName = localUser.UserName;
        var userRoot = localUser.Root;
        if (userRoot == null) 
        {
            return;
        }

        // Format element descriptions if present
        var formattedText = LogFormatter.FormatElementDescription(text);

        // Format timestamp at the start of the line
        formattedText = LogFormatter.FormatTimestamp(formattedText);

        // Format FPS if present in the text
        formattedText = LogFormatter.FormatFPS(formattedText);
        
        // Format any bracketed text in the message to be bold
        formattedText = LogFormatter.FormatBrackets(formattedText);
        
        // Apply color based on log level
        formattedText = LogFormatter.ApplyLogLevelColor(formattedText, level);
        
        var logMessage = string.IsNullOrEmpty(level) 
            ? formattedText
            : $"{LogFormatter.FormatLogLevel(level)} {formattedText}";

        // Process RTF tags (strip unwanted and fix unclosed) after all formatting is done
        logMessage = LogFormatter.ProcessRTFTags(logMessage);

        // Run world modifications synchronously
        world.RunSynchronously(() => {
            // Use ValueStream for real-time transmission
            UpdateValueStreams(localUser, userName, logMessage);
        });
    }

    private static void UpdateValueStreams(User localUser, string userName, string logMessage)
    {
        try
        {
            // Validate input parameters
            if (localUser == null)
            {
                ResoniteMod.Msg($"Failed to update ValueStream: localUser is null for {userName}");
                return;
            }

            if (string.IsNullOrEmpty(userName))
            {
                ResoniteMod.Msg("Failed to update ValueStream: userName is null or empty");
                return;
            }

            if (string.IsNullOrEmpty(logMessage))
            {
                ResoniteMod.Msg($"Failed to update ValueStream: logMessage is null or empty for {userName}");
                return;
            }

            // Check if handler already exists
            if (userLogHandlers.TryGetValue(userName, out var existingHandler))
            {
                // Validate existing handler before use
                if (existingHandler == null)
                {
                    ResoniteMod.Msg($"Failed to update ValueStream: existingHandler is null for {userName}");
                    userLogHandlers.TryRemove(userName, out _); // Clean up null handler
                    return;
                }

                if (!existingHandler.Initialized)
                {
                    ResoniteMod.Msg($"Failed to update ValueStream: existingHandler not initialized for {userName}");
                    return;
                }

                // Check if LogStream is still valid
                if (existingHandler.LogStream == null || existingHandler.LogStream.IsDestroyed || existingHandler.LogStream.IsDisposed)
                {
                    ResoniteMod.Msg($"[DEBUG] LogStream is null/destroyed/disposed for {userName}, removing handler");
                    existingHandler.Destroy();
                    userLogHandlers.TryRemove(userName, out _);
                    return;
                }

                // Check if UserInstance is still valid
                if (existingHandler.UserInstance == null || existingHandler.UserInstance != localUser)
                {
                    ResoniteMod.Msg($"[DEBUG] UserInstance mismatch for {userName}, removing handler");
                    existingHandler.Destroy();
                    userLogHandlers.TryRemove(userName, out _);
                    return;
                }

                try
                {
                    existingHandler.UpdateLog(logMessage);
                }
                catch (Exception updateEx)
                {
                    ResoniteMod.Msg($"Failed to update existing handler for {userName}: {updateEx.Message}");
                    // Try to recreate handler on next call
                    existingHandler.Destroy();
                    userLogHandlers.TryRemove(userName, out _);
                }
                return;
            }

            // Validate localUser state before creating new handler
            if (localUser.Root == null)
            {
                ResoniteMod.Msg($"Failed to update ValueStream: localUser.Root is null for {userName}");
                return;
            }

            // Create new handler only if one doesn't exist
            var newHandler = new LiveLogStreamHandler(localUser, maxLines, streamUpdatePeriod);
            
            try
            {
                newHandler.Setup();
            }
            catch (Exception e)
            {
                ResoniteMod.Msg($"Failed to setup LiveLogStreamHandler for {userName}: {e.Message}", true);
                newHandler.Destroy();
                return; // Exit early to prevent the loop
            }

            // Validate that setup was successful
            if (!newHandler.Initialized || newHandler.LogStream == null)
            {
                ResoniteMod.Msg($"Failed to initialize LiveLogStreamHandler for {userName}: Handler or LogStream is null after setup");
                newHandler.Destroy();
                return;
            }
            
            // Add to dictionary only after successful setup
            if (userLogHandlers.TryAdd(userName, newHandler))
            {
                // Handler created successfully
            }
            else
            {
                // Another thread created a handler, use that one
                if (userLogHandlers.TryGetValue(userName, out var handler))
                {
                    if (handler != null && handler.Initialized && handler.LogStream != null)
                    {
                        try
                        {
                            handler.UpdateLog(logMessage);
                        }
                        catch (Exception concurrentUpdateEx)
                        {
                            ResoniteMod.Msg($"Failed to update concurrent handler for {userName}: {concurrentUpdateEx.Message}");
                        }
                    }
                }
                // Clean up the handler we created
                newHandler.Destroy();
                return;
            }

            // Update log through handler
            try
            {
                newHandler.UpdateLog(logMessage);
            }
            catch (Exception logUpdateEx)
            {
                ResoniteMod.Msg($"Failed to update log for new handler {userName}: {logUpdateEx.Message}");
                newHandler.Destroy();
                userLogHandlers.TryRemove(userName, out _);
                return;
            }

            // Initialize user if needed
            if (!initializedUsers.Contains(userName))
            {
                initializedUsers.Add(userName);
            }
        }
        catch (Exception e)
        {
            ResoniteMod.Msg($"Failed to update ValueStream for {userName}: {e.Message}");
            // Log the full exception for debugging
            ResoniteMod.Msg($"ValueStream Exception Details: {e}");
        }
    }







    // Patch for Log(object, bool)
    [HarmonyPatch(typeof(UniLog))]
    [HarmonyPatch("Log", new Type[] { typeof(object), typeof(bool) })]
    public class UniLogObjectPatch
    { 
        public static void Postfix(object obj, bool stackTrace)
        {
            if (obj != null) ProcessLog(obj.ToString());
        }
    }

    // Patch for Log(string, bool)
    [HarmonyPatch(typeof(UniLog))]
    [HarmonyPatch("Log", new Type[] { typeof(string), typeof(bool) })]
    public class UniLogStringPatch
    {
        public static void Postfix(string message, bool stackTrace)
        {
            ProcessLog(message);
        }
    }

    // Patch for Warning(string, bool)
    [HarmonyPatch(typeof(UniLog))]
    [HarmonyPatch("Warning", new Type[] { typeof(string), typeof(bool) })]
    public class UniLogWarningPatch
    {
        public static void Postfix(string message, bool stackTrace)
        {
            ProcessLog(message, "WARNING");
        }
    }

    // Patch for Error(string, bool)
    [HarmonyPatch(typeof(UniLog))]
    [HarmonyPatch("Error", new Type[] { typeof(string), typeof(bool) })]
    public class UniLogErrorPatch
    {
        public static void Postfix(string message, bool stackTrace)
        {
            ProcessLog(message, "ERROR");
        }
    }


    public override void OnEngineInit()
    {
        // Get the config
        config = GetConfiguration();

        // Call setup method
        Setup();
    }

    private static void Setup()
    {
        
        // Patch Harmony
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll();
        
        // Subscribe to configuration changes using a single handler
        ModConfiguration.OnAnyConfigurationChanged += HandleConfigurationChanged;


        // Setup color config handlers
        colorConfigHandlers.Clear();
        colorConfigHandlers[timestampColorConfig] = (value) => ResoniteMod.Msg($"Timestamp color changed to: {value}");
        colorConfigHandlers[debugColorConfig] = (value) => ResoniteMod.Msg($"Debug color changed to: {value}");
        colorConfigHandlers[debugTextColorConfig] = (value) => ResoniteMod.Msg($"Debug text color changed to: {value}");
        colorConfigHandlers[infoColorConfig] = (value) => ResoniteMod.Msg($"Info color changed to: {value}");
        colorConfigHandlers[infoTextColorConfig] = (value) => ResoniteMod.Msg($"Info text color changed to: {value}");
        colorConfigHandlers[warningColorConfig] = (value) => ResoniteMod.Msg($"Warning color changed to: {value}");
        colorConfigHandlers[warningTextColorConfig] = (value) => ResoniteMod.Msg($"Warning text color changed to: {value}");
        colorConfigHandlers[errorColorConfig] = (value) => ResoniteMod.Msg($"Error color changed to: {value}");
        colorConfigHandlers[errorTextColorConfig] = (value) => ResoniteMod.Msg($"Error text color changed to: {value}");
        colorConfigHandlers[stackTraceAtColorConfig] = (value) => ResoniteMod.Msg($"Stack trace 'at' color changed to: {value}");
        colorConfigHandlers[stackTraceMethodColorConfig] = (value) => ResoniteMod.Msg($"Stack trace method color changed to: {value}");
        colorConfigHandlers[stackTraceTypeColorConfig] = (value) => ResoniteMod.Msg($"Stack trace type color changed to: {value}");
        colorConfigHandlers[fpsColorConfig] = (value) => ResoniteMod.Msg($"FPS color changed to: {value}");
        colorConfigHandlers[elementIdColorConfig] = (value) => ResoniteMod.Msg($"Element ID color changed to: {value}");
        colorConfigHandlers[elementTypeColorConfig] = (value) => ResoniteMod.Msg($"Element type color changed to: {value}");
        colorConfigHandlers[elementPropertyColorConfig] = (value) => ResoniteMod.Msg($"Element property color changed to: {value}");
        colorConfigHandlers[elementValueColorConfig] = (value) => ResoniteMod.Msg($"Element value color changed to: {value}");

        // Subscribe to all color config changes
        foreach (var kvp in colorConfigHandlers)
        {
            kvp.Key.OnChanged += kvp.Value;
        }

        // Subscribe to maxLines changes
        maxLinesConfig.OnChanged += maxLinesHandler;
        
        // Load configuration values
        maxLines = config.GetValue(maxLinesConfig);
        streamUpdatePeriod = config.GetValue(streamUpdatePeriodConfig);
        
        // Initialize LogFormatter with configuration
        LogFormatter.Initialize(config, 
            timestampColorConfig, debugColorConfig, debugTextColorConfig,
            infoColorConfig, infoTextColorConfig, warningColorConfig, warningTextColorConfig,
            errorColorConfig, errorTextColorConfig, stackTraceAtColorConfig,
            stackTraceMethodColorConfig, stackTraceTypeColorConfig, fpsColorConfig,
            elementIdColorConfig, elementTypeColorConfig, elementPropertyColorConfig, elementValueColorConfig);
        
        config.Save(true);

        Engine.Current.OnReady += () => {
            if (onReadyHandled) return;  // Skip if already handled
            onReadyHandled = true;  // Mark as handled
            
            engineInitialized = true;
            
            Msg("LiveLog mod initialized and ready!");
            
            // Try to create a handler immediately if user exists
            TryCreateInitialHandler();
        };
    }

    private static void CheckAndHandleWorldChange(World currentWorld)
    {
        if (lastKnownWorld == currentWorld)
        {
            return; // No world change
        }

        if (lastKnownWorld != null)
        {
            ResoniteMod.Msg($"World change detected! Cleaning up LiveLog handlers from previous world");
            
            // Clear all handlers from the previous world
            ClearAllHandlers();
            
            // Clear initialized users list
            initializedUsers.Clear();
        }

        lastKnownWorld = currentWorld;
        
        if (currentWorld != null)
        {
            ResoniteMod.Msg($"LiveLog ready for new world: {currentWorld.Name ?? "Unnamed"}");
        }
    }

    private static void ClearAllHandlers()
    {
        if (isCleaningUp) return; // Prevent recursive cleanup

        try
        {
            isCleaningUp = true;
            
            var handlersToDestroy = new List<LiveLogStreamHandler>(userLogHandlers.Values);
            userLogHandlers.Clear();

            foreach (var handler in handlersToDestroy)
            {
                try
                {
                    handler.Destroy();
                }
                catch (Exception ex)
                {
                    ResoniteMod.Msg($"[DEBUG] Error destroying handler during world change: {ex.Message}");
                }
            }

            ResoniteMod.Msg($"Cleaned up {handlersToDestroy.Count} LiveLog handlers");
        }
        catch (Exception e)
        {
            ResoniteMod.Msg($"[DEBUG] Failed to clear all handlers: {e.Message}");
        }
        finally
        {
            isCleaningUp = false;
        }
    }

    private static void TryCreateInitialHandler()
    {
        try
        {
            if (Engine.Current?.WorldManager?.FocusedWorld?.LocalUser == null)
            {
                return;
            }

            var world = Engine.Current.WorldManager.FocusedWorld;
            var localUser = world.LocalUser;
            var userName = localUser.UserName;

            if (localUser.Root == null)
            {
                return;
            }
            
            // Process a test log to trigger handler creation
            ProcessLog("[INFO] LiveLog mod ready", "INFO");
        }
        catch (Exception e)
        {
            ResoniteMod.Msg($"[DEBUG] Failed to create initial handler: {e.Message}");
        }
    }

    private static void HandleConfigurationChanged(ConfigurationChangedEvent configEvent)
    {
        // Handle any mod's configuration changes
        if (configEvent.Config != config)
        {
            return;
        }

        // Handle specific config changes
        if (configEvent.Key.Equals(maxLinesConfig.Name))
        {
            var newMax = config.GetValue(maxLinesConfig);
            UpdateMaxLines(newMax);
        }
        else if (configEvent.Key.Equals(streamUpdatePeriodConfig.Name))
        {
            var newUpdatePeriod = config.GetValue(streamUpdatePeriodConfig);
            streamUpdatePeriod = newUpdatePeriod;
            
            // Update existing streams with new period
            UpdateStreamPeriods();
        }
        else if (configEvent.Key.Equals(reloadLogsConfig.Name))
        {
            if (config.GetValue(reloadLogsConfig))
            {
                ReloadLogs();
                // Reset the toggle
                config.Set(reloadLogsConfig, false);
                config.Save();
            }
        }
        else if (configEvent.Key.Equals(clearLogsConfig.Name))
        {
            if (config.GetValue(clearLogsConfig))
            {
                ClearLogs();
                // Reset the toggle
                config.Set(clearLogsConfig, false);
                config.Save();
            }
        }
    }

    private static void ReloadLogs()
    {
        // Clear all handlers and let them rebuild from new logs
        foreach (var handler in userLogHandlers.Values)
        {
            handler.ClearLogs();
        }
    }

    private static void ClearLogs()
    {
        // Clear all handlers
        foreach (var handler in userLogHandlers.Values)
        {
            handler.ClearLogs();
        }
    }

    private static void ClearValueStreams()
    {
        try
        {
            foreach (var handler in userLogHandlers.Values)
            {
                handler.Destroy();
            }
            userLogHandlers.Clear();
        }
        catch (Exception e)
        {
            ResoniteMod.Msg($"Failed to clear ValueStreams: {e.Message}");
        }
    }

    private static void UpdateStreamPeriods()
    {
        try
        {
            foreach (var handler in userLogHandlers.Values)
            {
                handler.UpdateStreamPeriod(streamUpdatePeriod);
            }
        }
        catch (Exception e)
        {
            ResoniteMod.Msg($"Failed to update stream periods: {e.Message}");
        }
    }

    private static void CleanupEventHandlers()
    {
        // Unsubscribe from global config changes
        ModConfiguration.OnAnyConfigurationChanged -= HandleConfigurationChanged;

        // Unsubscribe from all color config changes
        foreach (var kvp in colorConfigHandlers)
        {
            kvp.Key.OnChanged -= kvp.Value;
        }
        colorConfigHandlers.Clear();

        // Unsubscribe from maxLines changes
        maxLinesConfig.OnChanged -= maxLinesHandler;
    }
}
