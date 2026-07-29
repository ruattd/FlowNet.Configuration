using System;
using FlowNet.Core;

namespace FlowNet.Configuration;

#pragma warning disable CS9113 // Attribute constructor arguments are consumed from metadata by source generators.

/// <summary>Marks the assembly as using Flow.NET Configuration.</summary>
[FlowExtensionUsage("global::FlowNet.Configuration.ConfigInterops.Initialize")]
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class UseConfigurationAttribute : Attribute;

/// <summary>Registers the configuration center configuration type.</summary>
[FlowExtensionUsage("global::FlowNet.Configuration.ConfigCenter.RegisterCenter")]
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AsConfigCenterAttribute(Type type) : Attribute;

/// <summary>Registers a configuration provider type.</summary>
[FlowExtensionUsage("global::FlowNet.Configuration.ConfigCenter.RegisterProvider")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AsConfigProviderAttribute(Type type) : Attribute;

/// <summary>Maps an instance partial property to a configuration identifier.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigPropertyAttribute(string identifier) : Attribute;

/// <summary>Maps a static partial property to a configuration identifier.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StaticConfigPropertyAttribute(string identifier) : Attribute;

#pragma warning restore CS9113
