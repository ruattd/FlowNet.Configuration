# FlowNet.Configuration

A Flow.NET configuration library with source-generated partial properties, prioritized providers, context-scoped caching, and optional queued writes.

## Install

Reference the `FlowNet.Configuration` package. The package includes the `FlowNet.Configuration.CodeAnalysis` source generator.

## Registration and initialization

Register exactly one `IConfigCenterOptions` implementation and any number of `IConfigProvider` implementations at assembly scope. Flow.NET discovers the attributes through `[UseConfiguration]`; the center is instantiated only when Flow.NET initializes extensions.

```csharp
using FlowNet.Configuration;

[assembly: UseConfiguration]
[assembly: AsConfigCenter(typeof(AppConfigOptions))]
[assembly: AsConfigProvider(typeof(EnvironmentConfigProvider))]
[assembly: AsConfigProvider(typeof(FileConfigProvider))]

sealed class AppConfigOptions : ConfigCenterOptions
{
    public override bool QueueWrites => true;
}
```

Provider reads and writes are tried independently in descending `GetPriority` and `SetPriority` order. A successful provider operation ends the attempt; failures fall through to the next provider.

```csharp
sealed class EnvironmentConfigProvider : ConfigProvider
{
    public override int GetPriority => 100;

    public override bool TryGet<T>(string identifier, out T value)
    {
        value = default!;
        return false;
    }
}
```

## Source-generated properties

Annotate properties in a `partial` class. Instance properties pass `this` as their typed context; static properties have no context.

```csharp
partial class Settings
{
    [ConfigProperty("ui.theme")]
    public partial string Theme { get; set; }

    [StaticConfigProperty("app.version")]
    public static partial string Version { get; set; }
}
```

The generator emits getters and setters that call `ConfigCenter`. Values are cached after a successful read or every write.

## ConfigCenter API

```csharp
// Context-free value
var name = ConfigCenter.Get<string>("app.name");
ConfigCenter.Set("app.name", "Flow.NET");

// Reference-type context: cache uses ConditionalWeakTable and does not retain context
var theme = ConfigCenter.Get<string, Settings>("ui.theme", settings);
ConfigCenter.Set<string, Settings>("ui.theme", settings, "dark");

// Value-type context
var enabled = ConfigCenter.GetValueContext<bool, int>("feature.enabled", tenantId);
ConfigCenter.SetValueContext<bool, int>("feature.enabled", tenantId, true);
```

Reference-type contexts use per-type `ConditionalWeakTable` caches. Value-type contexts use per-type concurrent dictionaries because value types cannot be weak-reference keys. Cached heterogeneous values use `FlowNet.ComponentModel.AnyValue`.

## Write queue and cleanup

When `QueueWrites` is `true`, writes are queued and a single worker waits on an `AutoResetEvent`; it does not poll. Call cleanup before rebuilding application configuration state:

```csharp
await ConfigCenter.Cleanup();
```

`Cleanup` blocks configuration operations, cancels and wakes the worker, waits for it to stop, and clears registrations and caches. Registration and initialization can then run again.

## Build

```sh
dotnet build FlowNet.Configuration.slnx
```

## License

Apache-2.0. See [LICENSE](LICENSE).
