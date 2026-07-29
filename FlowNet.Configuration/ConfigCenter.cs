using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using FlowNet.ComponentModel;
using System.Threading.Tasks;

namespace FlowNet.Configuration;

/// <summary>Coordinates registered configuration providers and caches configuration values.</summary>
public static class ConfigCenter
{

    private sealed class ContextCache
    {
        public ConcurrentDictionary<string, AnyValue> Values { get; } = new(StringComparer.Ordinal);
    }

    private abstract class PendingWrite
    {
        public abstract void Execute(IConfigProvider[] providers);
    }

    private sealed class PendingWrite<T>(string identifier, T value) : PendingWrite
    {
        public override void Execute(IConfigProvider[] providers) => TrySet(providers, identifier, value);
    }

    private sealed class PendingWrite<T, TContext>(string identifier, TContext context, T value) : PendingWrite
    {
        public override void Execute(IConfigProvider[] providers) => TrySet(providers, identifier, context, value);
    }

    private static class ReferenceContextCaches<TContext> where TContext : class
    {
        private static ConditionalWeakTable<TContext, ContextCache> _values = new();

        static ReferenceContextCaches() => CacheResetters.TryAdd(typeof(TContext), Reset);

        public static ContextCache Get(TContext context) => _values.GetValue(context, static _ => new ContextCache());
        private static void Reset() => _values = new ConditionalWeakTable<TContext, ContextCache>();
    }

    private static class ValueContextCaches<TContext> where TContext : struct
    {
        private static ConcurrentDictionary<TContext, ContextCache> _values = new();

        static ValueContextCaches() => CacheResetters.TryAdd(typeof(TContext), Reset);

        public static ContextCache Get(TContext context) => _values.GetOrAdd(context, static _ => new ContextCache());
        private static void Reset() => _values = new ConcurrentDictionary<TContext, ContextCache>();
    }

    private static readonly object Gate = new();
    private static readonly ReaderWriterLockSlim LifecycleGate = new();
    private static readonly AutoResetEvent WriteSignal = new(false);
    private static readonly List<Type> CenterTypes = [];
    private static readonly List<Type> ProviderTypes = [];
    private static readonly ConcurrentDictionary<string, AnyValue> StaticValues = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, Action> CacheResetters = new();
    private static readonly ConcurrentQueue<PendingWrite> PendingWrites = new();
    private static IConfigProvider[] _getProviders = [];
    private static IConfigProvider[] _setProviders = [];
    private static IConfigCenterOptions? _options;
    private static CancellationTokenSource? _writeCancellation;
    private static Task? _writeWorker;
    private static Task? _cleanupTask;
    private static int _initialized;
    private static bool _cleaning;

    /// <summary>Registers the configuration-center options type. Called by generated Flow.NET extension initialization.</summary>
    public static Task RegisterCenter(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        lock (Gate)
        {
            if (Volatile.Read(ref _initialized) != 0 || _cleaning) throw new InvalidOperationException("The configuration center is already initialized or being cleaned up.");
            CenterTypes.Add(type);
        }
        return Task.CompletedTask;
    }

    /// <summary>Registers a configuration provider type. Called by generated Flow.NET extension initialization.</summary>
    public static Task RegisterProvider(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        lock (Gate)
        {
            if (Volatile.Read(ref _initialized) != 0 || _cleaning) throw new InvalidOperationException("The configuration center is already initialized or being cleaned up.");
            ProviderTypes.Add(type);
        }
        return Task.CompletedTask;
    }

    /// <summary>Creates the registered center and providers. Exactly one center options type must be registered.</summary>
    public static Task Initialize()
    {
        lock (Gate)
        {
            if (_cleaning) throw new InvalidOperationException("The configuration center is being cleaned up.");
            if (Volatile.Read(ref _initialized) != 0) return Task.CompletedTask;
            if (CenterTypes.Count != 1) throw new InvalidOperationException("Exactly one configuration center type must be registered before initialization.");

            var options = Create<IConfigCenterOptions>(CenterTypes[0], "configuration center");
            var providers = ProviderTypes.Select(type => Create<IConfigProvider>(type, "configuration provider")).ToArray();
            _options = options;
            _getProviders = providers.OrderByDescending(provider => provider.GetPriority).ToArray();
            _setProviders = providers.OrderByDescending(provider => provider.SetPriority).ToArray();
            Volatile.Write(ref _initialized, 1);
            if (options.QueueWrites)
            {
                _writeCancellation = new CancellationTokenSource();
                _writeWorker = Task.Run(() => ProcessWrites(_setProviders, _writeCancellation.Token));
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Gets a cached or provider-supplied context-free configuration value.</summary>
    public static T Get<T>(string identifier)
    {
        LifecycleGate.EnterReadLock();
        try
        {
            EnsureInitialized();
            if (StaticValues.TryGetValue(identifier, out var cached)) return Read<T>(cached, identifier);
            foreach (var provider in _getProviders)
            {
                if (!provider.TryGet(identifier, out T value)) continue;
                StaticValues.TryAdd(identifier, AnyValue.Of(value));
                return value;
            }
            return default!;
        }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Gets a cached or provider-supplied value without retaining a reference-type context.</summary>
    public static T Get<T, TContext>(string identifier, TContext context) where TContext : class
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        LifecycleGate.EnterReadLock();
        try { return Get<T, TContext>(identifier, context, ReferenceContextCaches<TContext>.Get(context)); }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Gets a cached or provider-supplied value for a value-type context.</summary>
    public static T GetValueContext<T, TContext>(string identifier, TContext context) where TContext : struct
    {
        LifecycleGate.EnterReadLock();
        try { return Get<T, TContext>(identifier, context, ValueContextCaches<TContext>.Get(context)); }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Caches and persists a context-free configuration value.</summary>
    public static void Set<T>(string identifier, T value)
    {
        LifecycleGate.EnterReadLock();
        try
        {
            EnsureInitialized();
            StaticValues[identifier] = AnyValue.Of(value);
            if (_options!.QueueWrites)
            {
                PendingWrites.Enqueue(new PendingWrite<T>(identifier, value));
                WriteSignal.Set();
                return;
            }
            TrySet(_setProviders, identifier, value);
        }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Caches and persists a value without retaining a reference-type context.</summary>
    public static void Set<T, TContext>(string identifier, TContext context, T value) where TContext : class
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        LifecycleGate.EnterReadLock();
        try { Set(identifier, context, value, ReferenceContextCaches<TContext>.Get(context)); }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Caches and persists a value for a value-type context.</summary>
    public static void SetValueContext<T, TContext>(string identifier, TContext context, T value) where TContext : struct
    {
        LifecycleGate.EnterReadLock();
        try { Set(identifier, context, value, ValueContextCaches<TContext>.Get(context)); }
        finally { LifecycleGate.ExitReadLock(); }
    }

    /// <summary>Stops background work and removes all registered types, providers, and cached values.</summary>
    public static Task Cleanup()
    {
        lock (Gate)
        {
            if (_cleanupTask != null) return _cleanupTask;
            _cleaning = true;
            _cleanupTask = Task.Run(CleanupCoreAsync);
            return _cleanupTask;
        }
    }

    private static T Get<T, TContext>(string identifier, TContext context, ContextCache cache)
    {
        EnsureInitialized();
        if (cache.Values.TryGetValue(identifier, out var cached)) return Read<T>(cached, identifier);
        foreach (var provider in _getProviders)
        {
            if (!provider.TryGet(identifier, context, out T value)) continue;
            cache.Values.TryAdd(identifier, AnyValue.Of(value));
            return value;
        }
        return default!;
    }

    private static void Set<T, TContext>(string identifier, TContext context, T value, ContextCache cache)
    {
        EnsureInitialized();
        cache.Values[identifier] = AnyValue.Of(value);
        if (_options!.QueueWrites)
        {
            PendingWrites.Enqueue(new PendingWrite<T, TContext>(identifier, context, value));
            WriteSignal.Set();
            return;
        }
        TrySet(_setProviders, identifier, context, value);
    }

    private static async Task CleanupCoreAsync()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        LifecycleGate.EnterWriteLock();
        try
        {
            Volatile.Write(ref _initialized, 0);
            cancellation = _writeCancellation;
            worker = _writeWorker;
            _writeCancellation = null;
            _writeWorker = null;
            while (PendingWrites.TryDequeue(out _)) { }
            cancellation?.Cancel();
            WriteSignal.Set();
        }
        finally { LifecycleGate.ExitWriteLock(); }

        if (worker != null) await worker.ConfigureAwait(false);
        cancellation?.Dispose();

        LifecycleGate.EnterWriteLock();
        try
        {
            lock (Gate)
            {
                CenterTypes.Clear();
                ProviderTypes.Clear();
                StaticValues.Clear();
                foreach (var reset in CacheResetters.Values) reset();
                _getProviders = [];
                _setProviders = [];
                _options = null;
                _cleaning = false;
                _cleanupTask = null;
            }
        }
        finally { LifecycleGate.ExitWriteLock(); }
    }

    private static void ProcessWrites(IConfigProvider[] providers, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            WriteSignal.WaitOne();
            if (cancellationToken.IsCancellationRequested) return;
            while (!cancellationToken.IsCancellationRequested && PendingWrites.TryDequeue(out var write)) write.Execute(providers);
        }
    }

    private static void TrySet<T>(IConfigProvider[] providers, string identifier, T value)
    {
        foreach (var provider in providers)
            if (provider.TrySet(identifier, value)) return;
    }

    private static void TrySet<T, TContext>(IConfigProvider[] providers, string identifier, TContext context, T value)
    {
        foreach (var provider in providers)
            if (provider.TrySet(identifier, context, value)) return;
    }

    private static T Read<T>(AnyValue cached, string identifier) => cached.TryGet<T>(out var value)
        ? value
        : throw new InvalidCastException($"Configuration value '{identifier}' does not have type '{typeof(T)}'.");

    private static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 0) throw new InvalidOperationException("The configuration center has not been initialized.");
    }

    private static TContract Create<TContract>(Type type, string role) where TContract : class
    {
        if (!typeof(TContract).IsAssignableFrom(type)) throw new InvalidOperationException($"Registered {role} type '{type}' does not implement {typeof(TContract)}.");
        if (Activator.CreateInstance(type) is not TContract instance) throw new InvalidOperationException($"Registered {role} type '{type}' must have a public parameterless constructor.");
        return instance;
    }
}
