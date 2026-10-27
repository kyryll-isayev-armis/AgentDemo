using AgentDemo.Web.Game;

namespace AgentDemo.Web.Components;

public static class Names
{
    public static string Side(Player p) => p switch
    {
        Player.X => "VECTOR",
        Player.O => "ORBIT",
        _ => ""
    };

    public static string Tag(MatchSession? session, Player p)
    {
        if (session is null || session.Ai is null) return "";
        return p == session.HumanSide ? " (YOU)" : " (AI)";
    }

    public static string Rules(RuleSet r) => r == RuleSet.Decay ? "DECAY" : "CLASSIC";
}
