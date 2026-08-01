using Performa.Desktop.ViewModels;
using Xunit;

namespace Performa.Tests;

/// <summary>
/// The assistant used to send the same payload for every question: the full
/// commit list and every repo in the workspace, whatever was asked. Scoping it
/// is only safe if the routing is right, so this pins which slice each shape of
/// question pulls. A miss here is not a wasted token, it is the model answering
/// without the fact it needed.
/// </summary>
public class AssistantContextTests
{
    [Theory]
    [InlineData("what did i ship today")]
    [InlineData("did i commit anything")]
    [InlineData("is that done")]
    public void Shipping_questions_pull_the_commit_list(string q)
        => Assert.True(AssistantViewModel.FacetsFor(q).HasFlag(AssistantViewModel.Facet.Commits));

    [Theory]
    [InlineData("how is my week")]
    [InlineData("what's my streak")]
    [InlineData("am i keeping pace")]
    public void Pace_questions_pull_velocity(string q)
        => Assert.True(AssistantViewModel.FacetsFor(q).HasFlag(AssistantViewModel.Facet.Velocity));

    [Theory]
    [InlineData("what's left")]
    [InlineData("anything unfinished")]
    [InlineData("did i push that branch")]
    public void Loose_end_questions_pull_the_repo_list(string q)
        => Assert.True(AssistantViewModel.FacetsFor(q).HasFlag(AssistantViewModel.Facet.Repos));

    [Fact]
    public void A_streak_question_leaves_the_commit_list_behind()
    {
        var facets = AssistantViewModel.FacetsFor("what's my streak");
        Assert.False(facets.HasFlag(AssistantViewModel.Facet.Commits));
        Assert.False(facets.HasFlag(AssistantViewModel.Facet.Repos));
    }

    [Fact]
    public void A_question_spanning_two_topics_pulls_both()
    {
        var facets = AssistantViewModel.FacetsFor("what did i ship today and what's left");
        Assert.True(facets.HasFlag(AssistantViewModel.Facet.Commits));
        Assert.True(facets.HasFlag(AssistantViewModel.Facet.Repos));
    }

    [Fact]
    public void An_unrecognised_question_still_gets_somewhere_to_stand()
    {
        // Not None: the model needs orientation even when the routing misses.
        // Velocity is the cheapest of the three, being numbers rather than lists.
        var facets = AssistantViewModel.FacetsFor("what is the capital of peru");
        Assert.Equal(AssistantViewModel.Facet.Velocity, facets);
    }
}
