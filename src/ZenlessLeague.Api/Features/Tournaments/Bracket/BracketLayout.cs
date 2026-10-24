namespace ZenlessLeague.Api.Features.Tournaments.Bracket;

// A bracket's arrangement is just its round-1 slot list (null = bye): seeding is random, and
// admins rearrange it by hand before the tournament starts. Everything else is regenerated from it.
public static class BracketLayout
{
	// Shuffles the entrants and spreads the byes over distinct round-1 pairs. There are always
	// fewer byes than pairs (size is the smallest power of two >= the entrant count), so no pair
	// ever gets two.
	public static List<Guid?> Random(IReadOnlyList<Guid> entrants, Random rng)
	{
		var size = BracketEngine.SizeFor(entrants.Count);
		var pairCount = size / 2;

		var shuffled = entrants.ToArray();
		rng.Shuffle(shuffled);

		var pairs = Enumerable.Range(0, pairCount).ToArray();
		rng.Shuffle(pairs);
		var byePairs = pairs.Take(size - entrants.Count).ToHashSet();

		var slots = new List<Guid?>(size);
		var next = 0;
		for (var pair = 0; pair < pairCount; pair++)
		{
			if (!byePairs.Contains(pair))
			{
				slots.Add(shuffled[next++]);
				slots.Add(shuffled[next++]);
			}
			else if (rng.Next(2) == 0)
			{
				slots.Add(shuffled[next++]);
				slots.Add(null);
			}
			else
			{
				slots.Add(null);
				slots.Add(shuffled[next++]);
			}
		}

		return slots;
	}

	// An admin-submitted layout: exactly the bracket's size, every entrant exactly once, and no
	// round-1 pair of two byes (the engine would cope, but it'd hand someone a free second round).
	public static void Validate(IReadOnlyList<Guid?> slots, IReadOnlyCollection<Guid> entrants)
	{
		var size = BracketEngine.SizeFor(entrants.Count);
		if (slots.Count != size)
		{
			throw new BracketValidationException($"Layout must have exactly {size} slots for {entrants.Count} participants.");
		}

		var placed = slots.Where(s => s is not null).Select(s => s!.Value).ToList();
		if (placed.Count != entrants.Count || placed.Distinct().Count() != placed.Count || !placed.All(entrants.Contains))
		{
			throw new BracketValidationException("Layout must contain every participant exactly once.");
		}

		for (var i = 0; i < slots.Count; i += 2)
		{
			if (slots[i] is null && slots[i + 1] is null)
			{
				throw new BracketValidationException($"Slots {i + 1} and {i + 2} are both byes — two byes can't face each other in round 1.");
			}
		}
	}
}
