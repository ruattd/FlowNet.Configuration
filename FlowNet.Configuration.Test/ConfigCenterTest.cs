using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowNet.Configuration;

namespace FlowNet.Configuration.Test;

[TestClass]
public partial class ConfigCenterTest
{
    [TestMethod]
    public async Task GeneratedPropertiesUseContextAndPriorityOrderedCache()
    {
        await ConfigCenter.RegisterCenter(typeof(ImmediateOptions));
        await ConfigCenter.RegisterProvider(typeof(LowPriorityProvider));
        await ConfigCenter.RegisterProvider(typeof(HighPriorityProvider));
        await ConfigCenter.Initialize();

        var first = new Settings();
        var second = new Settings();

        Assert.AreEqual("high", first.Name);
        Assert.AreEqual("high", first.Name);
        Assert.AreEqual("high", second.Name);
        Assert.AreEqual(2, HighPriorityProvider.GetCalls);
        Assert.AreEqual(0, LowPriorityProvider.GetCalls);

        first.Name = "updated";
        Assert.AreEqual("updated", first.Name);
        Assert.AreEqual("high", second.Name);
        Assert.AreEqual(1, HighPriorityProvider.SetCalls);
        Assert.AreEqual(0, LowPriorityProvider.SetCalls);

        Settings.GlobalName = "static";
        Assert.AreEqual("static", Settings.GlobalName);
        await ConfigCenter.Cleanup();
        await ConfigCenter.RegisterCenter(typeof(QueuedOptions));
        await ConfigCenter.RegisterProvider(typeof(QueuedProvider));
        await ConfigCenter.Initialize();

        var queued = new Settings();
        queued.Name = "queued";
        await QueuedProvider.WriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual("queued", QueuedProvider.Value);

        await ConfigCenter.Cleanup();
        await ConfigCenter.RegisterCenter(typeof(ImmediateOptions));
        await ConfigCenter.RegisterProvider(typeof(HighPriorityProvider));
        await ConfigCenter.Initialize();
        Assert.AreEqual("high", new Settings().Name);
        await ConfigCenter.Cleanup();
    }

    private sealed class ImmediateOptions : ConfigCenterOptions
    {
        public override bool QueueWrites => false;
    }

    private sealed class QueuedOptions : ConfigCenterOptions
    {
        public override bool QueueWrites => true;
    }

    private sealed class QueuedProvider : ConfigProvider
    {
        public static TaskCompletionSource<bool> WriteCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static string? Value { get; private set; }

        public override bool TryGet<T, TContext>(string identifier, TContext context, out T value)
        {
            value = default!;
            return false;
        }

        public override bool TrySet<T, TContext>(string identifier, TContext context, T value)
        {
            Value = (string?)(object?)value;
            WriteCompleted.TrySetResult(true);
            return true;
        }
    }

    private sealed class HighPriorityProvider : ConfigProvider
    {
        public static int GetCalls;
        public static int SetCalls;

        public override int GetPriority => 10;
        public override int SetPriority => 10;
        public override bool TryGet<T, TContext>(string identifier, TContext context, out T value)
        {
            GetCalls++;
            value = (T)(object)"high";
            return true;
        }
        public override bool TrySet<T, TContext>(string identifier, TContext context, T value)
        {
            SetCalls++;
            return true;
        }
    }

    private sealed class LowPriorityProvider : ConfigProvider
    {
        public static int GetCalls;
        public static int SetCalls;

        public override int GetPriority => 0;

        public override bool TryGet<T, TContext>(string identifier, TContext context, out T value)
        {
            GetCalls++;
            value = (T)(object)"low";
            return true;
        }
        public override bool TrySet<T, TContext>(string identifier, TContext context, T value)
        {
            SetCalls++;
            return true;
        }
    }

    private partial class Settings
    {
        [ConfigProperty("settings.name")]
        public partial string Name { get; set; }

        [StaticConfigProperty("settings.global-name")]
        public static partial string GlobalName { get; set; }
    }
}
