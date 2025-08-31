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
        HandlerDict.TryAdd(UserName, this);
        
        // Create LiveLog slot
        LiveLogSlot = UserInstance.Root?.Slot.FindChild("LiveLogStream") ?? UserInstance.Root?.Slot.AddSlot("LiveLogStream");
        
        SetupLogStream();
        
        Initialized = true;
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
            
        // Add to queue
        logQueue.Enqueue(logMessage);
        
        // Keep only last maxLines lines
        while (logQueue.Count > maxLines && logQueue.TryDequeue(out _)) { }
        
        // Build the log content
        var logContent = string.Join("\n", logQueue.ToArray());
        
        // Update stream value
        LogStream.Value = logContent;
        LogStream.ForceUpdate(); // Force update like Resonance does
    }
    
    public void UpdateMaxLines(int newMaxLines)
    {
        // Trim existing logs to new limit
        while (logQueue.Count > newMaxLines && logQueue.TryDequeue(out _)) { }
    }
    
    public void UpdateStreamPeriod(int newPeriod)
    {
        if (Initialized)
        {
            LogStream.SetUpdatePeriod((uint)newPeriod, 0);
        }
    }
    
    public void ClearLogs()
    {
        while (logQueue.TryDequeue(out _)) { }
        LogStream.Value = "";
        LogStream.ForceUpdate();
    }
    
    public void Destroy()
    {
        HandlerDict.TryRemove(UserName, out _);
        LogStream?.Destroy();
        LiveLogSlot?.Destroy();
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
