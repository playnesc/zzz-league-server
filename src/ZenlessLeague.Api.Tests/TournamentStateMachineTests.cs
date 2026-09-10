using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class TournamentStateMachineTests
{
	[Theory]
	[InlineData(TournamentState.Registration, true)]
	[InlineData(TournamentState.RegistrationClosed, false)]
	[InlineData(TournamentState.BracketCreated, false)]
	public void IsRegistrationOpen_OnlyTrueDuringRegistration(TournamentState state, bool expected) =>
		Assert.Equal(expected, TournamentStateMachine.IsRegistrationOpen(state));

	[Theory]
	[InlineData(TournamentState.Registration, false)]
	[InlineData(TournamentState.RegistrationClosed, false)]
	[InlineData(TournamentState.BracketCreated, true)]
	[InlineData(TournamentState.Started, true)]
	[InlineData(TournamentState.AwaitingReview, true)]
	[InlineData(TournamentState.Complete, true)]
	public void IsLocked_TrueFromBracketCreationOnward(TournamentState state, bool expected) =>
		Assert.Equal(expected, TournamentStateMachine.IsLocked(state));

	[Theory]
	[InlineData(TournamentState.BracketCreated, false)]
	[InlineData(TournamentState.Started, true)]
	[InlineData(TournamentState.AwaitingReview, true)]
	[InlineData(TournamentState.Complete, true)]
	public void HasStarted_TrueFromStartedOnward(TournamentState state, bool expected) =>
		Assert.Equal(expected, TournamentStateMachine.HasStarted(state));
}
