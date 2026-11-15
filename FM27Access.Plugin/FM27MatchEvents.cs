using SI.MatchTypes;
namespace FM27Access.Plugin;

// FM27 removed FM.Match.VisualsSubstitution and the corresponding extension methods.
// Match only the replacement events present in the current game's generated enum.
internal static class FM27MatchEvents
{
    internal static int SubstitutionPairs(CommentaryEventDefinitions id)=>id switch
    {
        CommentaryEventDefinitions.EventSubstitution or CommentaryEventDefinitions.EventSubstitutionAtHalfTime=>1,
        CommentaryEventDefinitions.EventDoubleSubstitution or CommentaryEventDefinitions.EventDoubleSubstitutionAtHalfTime=>2,
        CommentaryEventDefinitions.EventTripleSubstitution or CommentaryEventDefinitions.EventTripleSubstitutionAtHalfTime=>3,
        _=>0
    };

    internal static int SubstitutedOffSlot(CommentaryEventDefinitions id, int pair)
    {
        int count = SubstitutionPairs(id);
        if (pair < 0 || pair >= count) return -1;
        // Observed against both lineups in the Leicester and Sheffield QA
        // matches: halftime doubles/triples group incoming, then outgoing players.
        // Ordinary single/double/triple events use interleaved (on, off) pairs.
        return id is CommentaryEventDefinitions.EventDoubleSubstitutionAtHalfTime
            or CommentaryEventDefinitions.EventTripleSubstitutionAtHalfTime
            ? count + pair : pair * 2 + 1;
    }
}
