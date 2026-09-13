namespace ZenlessLeague.Api.Features.CommunityCosts.Catalog;

// Shared by both the agent and engine catalogs — an engine's specialty says which agent
// specialty it's built for, mirroring the frontend's costData.ts Specialty union type.
// Armorer is a single hand-placed exception (one agent, no matching engines).
public enum Specialty { Attack, Stun, Anomaly, Support, Defense, Rupture, Armorer }
