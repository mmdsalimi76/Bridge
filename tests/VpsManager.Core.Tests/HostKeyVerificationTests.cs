using System;
using System.IO;
using System.Threading.Tasks;
using VpsManager.Core.Security;

namespace VpsManager.Core.Tests;

public class HostKeyVerificationTests : IDisposable
{
    private readonly string _storePath = Path.Combine(
        Path.GetTempPath(),
        $"vpsmanager-knownhosts-{Guid.NewGuid():N}.json");

    private static HostKeyInfo Key(string host = "10.0.0.5", int port = 22, string fingerprint = "SHA256:aaa") =>
        new(host, port, "ssh-ed25519", fingerprint);

    private sealed class StubPrompt : IHostKeyTrustPrompt
    {
        public bool TrustUnknown { get; set; }
        public bool TrustChanged { get; set; }
        public int UnknownCalls { get; private set; }
        public int ChangedCalls { get; private set; }

        public bool ShouldTrustUnknownHost(HostKeyInfo hostKey)
        {
            UnknownCalls++;
            return TrustUnknown;
        }

        public bool ShouldTrustChangedHost(HostKeyInfo hostKey, string previousFingerprint)
        {
            ChangedCalls++;
            return TrustChanged;
        }
    }

    [Fact]
    public void UnknownHost_WithoutPrompt_IsRejected()
    {
        var policy = new HostKeyPolicy(new JsonKnownHostsStore(_storePath));

        var decision = policy.Evaluate(Key());

        Assert.False(decision.Allow);
        Assert.Contains("not trusted yet", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownHost_WhenUserTrusts_IsStoredAndAccepted()
    {
        var store = new JsonKnownHostsStore(_storePath);
        var prompt = new StubPrompt { TrustUnknown = true };
        var policy = new HostKeyPolicy(store, prompt);

        var decision = policy.Evaluate(Key());

        Assert.True(decision.Allow);
        Assert.Equal(1, prompt.UnknownCalls);
        Assert.True(store.TryGetFingerprint("10.0.0.5", 22, out var stored));
        Assert.Equal("SHA256:aaa", stored);
    }

    [Fact]
    public void KnownHost_WithMatchingKey_IsAcceptedWithoutPrompting()
    {
        var store = new JsonKnownHostsStore(_storePath);
        store.Store(Key());
        var prompt = new StubPrompt { TrustUnknown = true, TrustChanged = true };
        var policy = new HostKeyPolicy(store, prompt);

        var decision = policy.Evaluate(Key());

        Assert.True(decision.Allow);
        Assert.Equal(0, prompt.UnknownCalls);
        Assert.Equal(0, prompt.ChangedCalls);
    }

    [Fact]
    public void KnownHost_WithChangedKey_IsRejectedWhenUserDeclines()
    {
        var store = new JsonKnownHostsStore(_storePath);
        store.Store(Key(fingerprint: "SHA256:old"));
        var prompt = new StubPrompt { TrustChanged = false };
        var policy = new HostKeyPolicy(store, prompt);

        var decision = policy.Evaluate(Key(fingerprint: "SHA256:new"));

        Assert.False(decision.Allow);
        Assert.Equal(1, prompt.ChangedCalls);
        Assert.Contains("has changed", decision.Reason, StringComparison.Ordinal);
        Assert.True(store.TryGetFingerprint("10.0.0.5", 22, out var stored));
        Assert.Equal("SHA256:old", stored);
    }

    [Fact]
    public void KnownHost_WithChangedKey_IsAcceptedOnlyOnExplicitApproval()
    {
        var store = new JsonKnownHostsStore(_storePath);
        store.Store(Key(fingerprint: "SHA256:old"));
        var policy = new HostKeyPolicy(store, new StubPrompt { TrustChanged = true });

        var decision = policy.Evaluate(Key(fingerprint: "SHA256:new"));

        Assert.True(decision.Allow);
        Assert.True(store.TryGetFingerprint("10.0.0.5", 22, out var stored));
        Assert.Equal("SHA256:new", stored);
    }

    [Fact]
    public void TrustedKeys_PersistAcrossStoreInstances()
    {
        var first = new JsonKnownHostsStore(_storePath);
        var policy = new HostKeyPolicy(first, new StubPrompt { TrustUnknown = true });
        policy.Evaluate(Key());

        var reloaded = new JsonKnownHostsStore(_storePath);

        Assert.True(reloaded.TryGetFingerprint("10.0.0.5", 22, out var stored));
        Assert.Equal("SHA256:aaa", stored);
    }

    [Fact]
    public void CorruptStore_FailsClosed()
    {
        File.WriteAllText(_storePath, "{ this is not valid json");
        var store = new JsonKnownHostsStore(_storePath);
        var policy = new HostKeyPolicy(store);

        var decision = policy.Evaluate(Key());

        Assert.False(decision.Allow);
    }

    [Fact]
    public void Forget_RemovesTrustedKey()
    {
        var store = new JsonKnownHostsStore(_storePath);
        store.Store(Key());

        Assert.True(store.Forget("10.0.0.5", 22));
        Assert.False(store.TryGetFingerprint("10.0.0.5", 22, out _));
    }

    public void Dispose()
    {
        if (File.Exists(_storePath))
        {
            File.Delete(_storePath);
        }
    }
}
