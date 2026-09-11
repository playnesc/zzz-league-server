namespace ZenlessLeague.Api.Features.Tournaments;

public static class TournamentStateMachine
{
	public static bool IsRegistrationOpen(TournamentState state) => state == TournamentState.Registration;

	public static bool IsBracketCreated(TournamentState state) => state == TournamentState.BracketCreated;

	public static bool HasStarted(TournamentState state) =>
		state is TournamentState.Started or TournamentState.AwaitingReview or TournamentState.Complete;

	// True once the Challonge bracket exists (participants locked in), whether or not the
	// tournament itself has started yet — registration/roster changes are blocked from here on.
	public static bool IsLocked(TournamentState state) => IsBracketCreated(state) || HasStarted(state);
}
