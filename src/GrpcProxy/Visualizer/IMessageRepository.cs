﻿using GrpcProxy.Data;

namespace GrpcProxy.Visualizer;

public interface IMessageRepositoryIngress
{
    /// <summary>
    /// Adds a message to the message repository.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public Task AddAsync(ProxyMessage item);
}


public interface IMessageRepository<TMessage> : IMessageRepositoryIngress
{
    /// <summary>
    /// Returns an immutable list of messages.
    /// </summary>
    public ICollection<TMessage> Messages { get; }

    /// <summary>
    /// Returns if the current repository has paused processing messages.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// This event is fired when a new message is available in the repository.
    /// </summary>
    public event EventHandler<TMessage> OnMessage;

    /// <summary>
    /// Remove all messages from the repository.
    /// </summary>
    public void Clear();

    /// <summary>
    /// Pauses message processing
    /// </summary>
    public void Disable();

    /// <summary>
    /// Resumes message processing
    /// </summary>
    public void Enable();
}
