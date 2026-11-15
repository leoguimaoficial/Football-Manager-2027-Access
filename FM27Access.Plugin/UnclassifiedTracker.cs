using System.Text;

namespace FM27Access.Plugin;

internal static class UnclassifiedTracker
{
    public static readonly Dictionary<string, UnclassifiedEntry> Entries = new(StringComparer.Ordinal);

    public class UnclassifiedEntry
    {
        public string TypeName;
        public int HitCount;
        public string[] Interfaces = Array.Empty<string>();
        public string[] UssClasses = Array.Empty<string>();
        public string ExampleName = "";
        public readonly HashSet<string> PanelTitles = new();
    }

    public static void Track(string typeName, string elementName, string[] interfaces, string[] ussClasses, string panelTitle)
    {
        if (!Entries.TryGetValue(typeName, out var entry))
        {
            entry = new UnclassifiedEntry
            {
                TypeName = typeName,
                Interfaces = interfaces,
                UssClasses = ussClasses,
                ExampleName = elementName,
            };
            Entries[typeName] = entry;

            Speech.Log.Info($"[UnclassifiedTracker] NEW unclassified type: {typeName} name=\"{elementName}\" interfaces=[{string.Join(",", interfaces)}] uss=[{string.Join(",", ussClasses)}]");
        }

        entry.HitCount++;
        // Capped: this set was the one collection in the mod that grew without bound (every
        // distinct panel title ever seen, per type, for the whole session), and RenderText only
        // ever shows the first five anyway.
        if (!string.IsNullOrEmpty(panelTitle) && entry.PanelTitles.Count < 10)
            entry.PanelTitles.Add(panelTitle);
    }

    // Render the accumulated unknowns as text, shared by the F11 file dump and the /unclassified
    // endpoint. The tracker is stateful across every screen visited, which is why it catches a
    // widget the classifier silently skips - something a single-panel /classify snapshot cannot.
    public static string RenderText()
    {
        if (Entries.Count == 0) return "no unclassified elements tracked\n";
        var sb = new StringBuilder();
        sb.Append(Entries.Count).Append(" unique unclassified element type(s) since last clear:\n");
        foreach (var e in Entries.Values.OrderByDescending(e => e.HitCount))
            sb.Append($"  type={e.TypeName} hits={e.HitCount} interfaces=[{string.Join(", ", e.Interfaces)}] uss=[{string.Join(", ", e.UssClasses)}] panels=[{string.Join(", ", e.PanelTitles.Take(5))}] example_name=\"{e.ExampleName}\"\n");
        return sb.ToString();
    }
}
