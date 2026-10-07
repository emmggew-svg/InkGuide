using System;
using System.Collections.Generic;
using InkGuide.Application.Tags;
using InkGuide.Domain.Entities;
using Xunit;

namespace InkGuide.UnitTests.Application;

public sealed class TagPolicyTests
{
    [Fact]
    public void DirectAssignmentsResolve()
    {
        Tag tag = Tag("t1", "review");

        IReadOnlyList<Tag> effective = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(tag, false) },
            Array.Empty<TagSuppression>(),
            "n1");

        Assert.Single(effective);
        Assert.Same(tag, effective[0]);
    }

    [Fact]
    public void InheritedTagsResolveAlongsideDirectTags()
    {
        Tag direct = Tag("t1", "review");
        Tag inherited = Tag("t2", "type:web-site");

        IReadOnlyList<Tag> effective = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(direct, false), new TagAssignment(inherited, true) },
            Array.Empty<TagSuppression>(),
            "n1");

        Assert.Equal(2, effective.Count);
    }

    [Fact]
    public void WorkspaceWideSuppressionDropsAutoTags()
    {
        Tag auto = Tag("t1", "type:ads", TagOrigin.Auto);
        TagSuppression suppression = new() { WorkspaceId = "w1", NormalizedTagName = "TYPE:ADS" };

        IReadOnlyList<Tag> effective = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(auto, true) },
            new[] { suppression },
            "n1");

        Assert.Empty(effective);
    }

    [Fact]
    public void NodeScopedSuppressionIsLocal()
    {
        Tag auto = Tag("t1", "type:ads", TagOrigin.Auto);
        TagSuppression suppression = new() { WorkspaceId = "w1", NormalizedTagName = "TYPE:ADS", NodeId = "n2" };

        IReadOnlyList<Tag> other = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(auto, true) },
            new[] { suppression },
            "n1");
        IReadOnlyList<Tag> scoped = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(auto, true) },
            new[] { suppression },
            "n2");

        Assert.Single(other);
        Assert.Empty(scoped);
    }

    [Fact]
    public void DuplicatesKeepTheLeafFirstWinner()
    {
        Tag direct = Tag("t1", "Type:Web-Site", TagOrigin.User);
        Tag inherited = Tag("t2", "type:web-site", TagOrigin.Auto);

        IReadOnlyList<Tag> effective = TagPolicy.ResolveEffectiveTags(
            new[] { new TagAssignment(direct, false), new TagAssignment(inherited, true) },
            Array.Empty<TagSuppression>(),
            "n1");

        Assert.Single(effective);
        Assert.Same(direct, effective[0]);
    }

    [Fact]
    public void SuppressionCheckIsCaseInsensitive()
    {
        TagSuppression suppression = new() { WorkspaceId = "w1", NormalizedTagName = "TYPE:ADS" };

        Assert.True(TagPolicy.IsSuppressed("type:ads", "n1", new[] { suppression }));
        Assert.False(TagPolicy.IsSuppressed("type:docs", "n1", new[] { suppression }));
        Assert.False(TagPolicy.IsSuppressed("type:ads", "n1", Array.Empty<TagSuppression>()));
    }

    private static Tag Tag(string id, string name, TagOrigin origin = TagOrigin.User)
    {
        return new Tag { Id = id, WorkspaceId = "w1", Name = name, Origin = origin };
    }
}
