using FrooxEngine;
using Elements.Core;
using System;
using System.Collections.Concurrent;
using System.Linq;
using ResoniteModLoader;

namespace LiveLogStream;

public class LiveLogStreamHandler
{
    public static ConcurrentDictionary<string, LiveLogStreamHandler> HandlerDict = new();
    
    public readonly User UserInstance;
    public readonly string UserName;
    public bool Initialized { get; private set; }
    
    // Stream management
    public ValueStream<string> LogStream { get; private set; }
    
    // Log data management
    private readonly ConcurrentQueue<string> logQueue = new();
    private readonly int maxLines;
    private readonly int updatePeriod;
    
    // LiveLog slot
    public Slot? LiveLogSlot { get; private set; }
    
    // Constants
    public const string LOG_STREAM_VARIABLE = "User/livelog_stream";
    
    public LiveLogStreamHandler(User user, int maxLines, int updatePeriod)
    {
        UserInstance = user ?? throw new NullReferenceException("LiveLogStreamHandler REQUIRES a User instance!");
        UserName = user.UserName;
        this.maxLines = maxLines;
        this.updatePeriod = updatePeriod;
    }
    
    public void Setup()
    {
        if (UserInstance?.Root?.Slot == null)
        {
            throw new InvalidOperationException("UserInstance.Root.Slot is null - cannot create LiveLog slot");
        }

        HandlerDict.TryAdd(UserName, this);
        
        try
        {
            // Create LiveLog slot
            var existingSlot = UserInstance.Root.Slot.FindChild("LiveLogStream");
            if (existingSlot != null)
            {
                LiveLogSlot = existingSlot;
            }
            else
            {
                LiveLogSlot = UserInstance.Root.Slot.AddSlot("LiveLogStream");
                ResoniteMod.Msg($"Created LiveLogStream slot for {UserName}");
            }
            
            if (LiveLogSlot == null)
            {
                throw new InvalidOperationException("Failed to create or find LiveLogStream slot");
            }
            
            // Make slot visible and persistent
            LiveLogSlot.PersistentSelf = true;
            
            SetupLogStream();
            
            if (LogStream == null)
            {
                throw new InvalidOperationException("LogStream is null after SetupLogStream");
            }
            
            Initialized = true;
        }
        catch (Exception)
        {
            // Clean up on failure
            Initialized = false;
            LiveLogSlot?.Destroy();
            LiveLogSlot = null;
            LogStream?.Destroy();
            LogStream = null;
            HandlerDict.TryRemove(UserName, out _);
            throw; // Re-throw to let caller handle
        }
    }
    
    private void SetupLogStream()
    {
        // Create stream with proper naming pattern like Resonance - using a unique identifier
        var streamName = $"{UserInstance.ReferenceID}.livelog.{UserName}";
        
        LogStream = UserInstance.GetStreamOrAdd<ValueStream<string>>(streamName, SetStreamParams);
        
        // Create reference variable in LiveLog slot
        if (LiveLogSlot != null)
        {
            LiveLogSlot.CreateReferenceVariable<IValue<string>>(LOG_STREAM_VARIABLE, LogStream, true);
        }
    }
    
    private void SetStreamParams(ValueStream<string> stream)
    {
        stream.Name = "LiveLog Stream";
        stream.SetInterpolation();
        stream.SetUpdatePeriod((uint)updatePeriod, 0);
        // String values only support full encoding, so we don't need to set it explicitly
        // stream.Encoding = ValueEncoding.Full; // This was causing the error
    }
    
    public void UpdateLog(string logMessage)
    {
        if (!Initialized)
            throw new LiveLogStreamNotInitializedException();

        if (LogStream == null)
            throw new InvalidOperationException("LogStream is null - handler may have been destroyed or not properly initialized");

        if (string.IsNullOrEmpty(logMessage))
            return; // Skip empty messages
            
        // Add to queue
        logQueue.Enqueue(logMessage);
        
        // Keep only last maxLines lines
        while (logQueue.Count > maxLines && logQueue.TryDequeue(out _)) { }
        
        // Build the log content
        var logContent = string.Join("\n", logQueue.ToArray());
        
        try
        {
            // Update stream value
            LogStream.Value = logContent;
            LogStream.ForceUpdate(); // Force update like Resonance does
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update LogStream: {ex.Message}", ex);
        }
    }
    
    public void UpdateMaxLines(int newMaxLines)
    {
        // Trim existing logs to new limit
        while (logQueue.Count > newMaxLines && logQueue.TryDequeue(out _)) { }
    }
    
    public void UpdateStreamPeriod(int newPeriod)
    {
        if (Initialized && LogStream != null)
        {
            try
            {
                LogStream.SetUpdatePeriod((uint)newPeriod, 0);
            }
            catch (Exception ex)
            {
                ResoniteMod.Error($"Failed to update stream period for {UserName}: {ex.Message}");
            }
        }
    }
    
    public void ClearLogs()
    {
        while (logQueue.TryDequeue(out _)) { }
        
        if (LogStream != null)
        {
            try
            {
                LogStream.Value = "";
                LogStream.ForceUpdate();
            }
            catch (Exception ex)
            {
                ResoniteMod.Error($"Failed to clear logs for {UserName}: {ex.Message}");
            }
        }
    }
    
    public void Destroy()
    {
        HandlerDict.TryRemove(UserName, out _);
        
        // Safely destroy LogStream - check if it's not disposed first
        if (LogStream != null)
        {
            try
            {
                // Check if the stream is still valid before destroying
                if (!LogStream.IsDestroyed && !LogStream.IsDisposed)
                {
                    LogStream.Destroy();
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't let it propagate - this is cleanup code
                ResoniteMod.Msg($"[DEBUG] Error destroying LogStream for {UserName}: {ex.Message}");
            }
            LogStream = null;
        }
        
        // Safely destroy LiveLogSlot
        if (LiveLogSlot != null)
        {
            try
            {
                if (!LiveLogSlot.IsDestroyed && !LiveLogSlot.IsDisposed)
                {
                    LiveLogSlot.Destroy();
                }
            }
            catch (Exception ex)
            {
                ResoniteMod.Msg($"[DEBUG] Error destroying LiveLogSlot for {UserName}: {ex.Message}");
            }
            LiveLogSlot = null;
        }
        
        Initialized = false;
    }
    
    public static void Destroy(string userName)
    {
        if (HandlerDict.TryRemove(userName, out var handler))
        {
            handler.Destroy();
        }
    }
}

[Serializable]
public class LiveLogStreamNotInitializedException : Exception
{
    public LiveLogStreamNotInitializedException() : base("LiveLog stream handler is not initialized! Did you run Setup() after creating the LiveLogStreamHandler?") { }
    public LiveLogStreamNotInitializedException(string message) : base(message) { }
    public LiveLogStreamNotInitializedException(string message, Exception inner) : base(message, inner) { }
    protected LiveLogStreamNotInitializedException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
