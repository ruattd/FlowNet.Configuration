namespace FlowNet.Configuration;

/// <summary>Defines the configurable behavior of <see cref="ConfigCenter"/>.</summary>
public interface IConfigCenterOptions
{
    /// <summary>Whether writes are queued instead of executed inline.</summary>
    bool QueueWrites { get; }

}

/// <summary>Provides default configuration-center behavior.</summary>
public abstract class ConfigCenterOptions : IConfigCenterOptions
{
    /// <inheritdoc />
    public virtual bool QueueWrites => true;

}

/// <summary>Reads and writes configuration values for a particular backend.</summary>
public interface IConfigProvider
{
    /// <summary>Higher values are tried first when reading.</summary>
    int GetPriority { get; }

    /// <summary>Higher values are tried first when writing.</summary>
    int SetPriority { get; }

    /// <summary>Tries to read a context-free value.</summary>
    bool TryGet<T>(string identifier, out T value);

    /// <summary>Tries to read a context-bound value.</summary>
    bool TryGet<T, TContext>(string identifier, TContext context, out T value);

    /// <summary>Tries to write a context-free value.</summary>
    bool TrySet<T>(string identifier, T value);

    /// <summary>Tries to write a context-bound value.</summary>
    bool TrySet<T, TContext>(string identifier, TContext context, T value);
}

/// <summary>Convenience base class for configuration providers.</summary>
public abstract class ConfigProvider : IConfigProvider
{
    /// <inheritdoc />
    public virtual int GetPriority => 0;

    /// <inheritdoc />
    public virtual int SetPriority => 0;

    /// <inheritdoc />
    public virtual bool TryGet<T>(string identifier, out T value)
    {
        value = default!;
        return false;
    }

    /// <inheritdoc />
    public virtual bool TryGet<T, TContext>(string identifier, TContext context, out T value)
    {
        value = default!;
        return false;
    }

    /// <inheritdoc />
    public virtual bool TrySet<T>(string identifier, T value) => false;

    /// <inheritdoc />
    public virtual bool TrySet<T, TContext>(string identifier, TContext context, T value) => false;
}
