using Jellyfin.Plugin.QualityBitrateManager.Configuration;
using Jellyfin.Plugin.QualityBitrateManager.Helpers;
using Jellyfin.Plugin.QualityBitrateManager.Models;
using Jellyfin.Plugin.QualityBitrateManager.Services;
using Xunit;

namespace Jellyfin.Plugin.QualityBitrateManager.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData(3840,2160,QualityTier.P2160)][InlineData(3840,1600,QualityTier.P2160)][InlineData(2560,1440,QualityTier.P1440)]
    [InlineData(1920,1080,QualityTier.P1080)][InlineData(1920,800,QualityTier.P1080)][InlineData(1280,720,QualityTier.P720)][InlineData(854,480,QualityTier.P480)]
    public void ClassifiesResolution(int width,int height,QualityTier expected) => Assert.Equal(expected, QualityClassifier.Classify(width,height));

    [Fact] public void PolicyUsesEnabledRuleAndConvertsUnits() { var c=new PluginConfiguration { Enable1080p=true, Bitrate1080pMbps=12 }; Assert.Equal(12_000_000,new BitratePolicyService().GetLimit(QualityTier.P1080,c)); }
    [Fact] public void PolicyConvertsDecimalMegabits() { var c=new PluginConfiguration { Enable1080p=true, Bitrate1080pMbps=3.5m }; Assert.Equal(3_500_000,new BitratePolicyService().GetLimit(QualityTier.P1080,c)); }
    [Fact] public void DisabledRuleUsesDefault() { var c=new PluginConfiguration { StandardBitrateMbps=20, Enable1080p=false }; Assert.Equal(20_000_000,new BitratePolicyService().GetLimit(QualityTier.P1080,c)); }

    [Fact] public void TrackerIsIdempotentAndUsesLowestLimit()
    {
        var t=new ActivePlaybackTracker();var u=Guid.NewGuid();var item=Guid.NewGuid();
        t.Upsert(u,new("a","s1",item,QualityTier.P2160,35_000_000,DateTimeOffset.UtcNow));
        t.Upsert(u,new("a","s1",item,QualityTier.P2160,35_000_000,DateTimeOffset.UtcNow));
        var result=t.Upsert(u,new("b","s2",item,QualityTier.P1080,12_000_000,DateTimeOffset.UtcNow));
        Assert.Equal((12_000_000L,2),result); Assert.Equal((35_000_000L,1),t.Remove(u,"b")); Assert.Equal((null,0),t.Remove(u,"a")); Assert.Equal((null,0),t.Remove(u,"a"));
    }

    [Fact] public async Task TrackerHandlesConcurrentEvents()
    {
        var t=new ActivePlaybackTracker();var u=Guid.NewGuid();var item=Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0,100).Select(i=>Task.Run(()=>t.Upsert(u,new(i.ToString(),"s",item,QualityTier.P720,6_000_000,DateTimeOffset.UtcNow)))));
        Assert.Equal(6_000_000,t.GetEffectiveLimit(u));
        await Task.WhenAll(Enumerable.Range(0,100).Select(i=>Task.Run(()=>t.Remove(u,i.ToString())))); Assert.Null(t.GetEffectiveLimit(u));
    }

    [Fact] public void PendingTrackerConsumesAndExpiresReservations()
    {
        var tracker=new PendingPlaybackTracker();var user=Guid.NewGuid();var item=Guid.NewGuid();
        tracker.Add(user,item,6_000_000,TimeSpan.FromMinutes(1));
        tracker.Add(user,Guid.NewGuid(),3_000_000,TimeSpan.FromSeconds(-1));
        Assert.Equal(3_000_000,tracker.GetEffectiveLimit(user));
        Assert.Contains(user,tracker.RemoveExpired(DateTimeOffset.UtcNow));
        Assert.Equal(6_000_000,tracker.GetEffectiveLimit(user));
        tracker.Consume(user,item); Assert.Null(tracker.GetEffectiveLimit(user));
    }
}
