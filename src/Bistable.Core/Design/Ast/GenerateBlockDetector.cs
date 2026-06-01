using System.Text.RegularExpressions;

namespace Bistable.Core.Design.Ast;

/// <summary>
/// P2.6-2: detects <c>generate for</c> unrollings in a module's instance list.
/// Verilator names unrolled cells like <c>g[0].inst</c>, <c>g[1].inst</c>, …,
/// <c>g[7].inst</c> for a <c>generate ... for (i=0;i&lt;8;i++) begin: g; ... end</c>
/// block. The detector groups them so downstream consumers (schematic
/// renderer, hierarchy panel) can collapse the repeats into one "deck of
/// cards" symbol with N labelled iterations.
/// </summary>
/// <remarks>
/// The detector is a pure function — it does not modify the AST. Callers that
/// want a grouped view can call <see cref="DetectGroups"/> and filter the
/// raw instance list against the returned groups' member sets.
/// </remarks>
public static class GenerateBlockDetector
{
    // Matches "g[0].inst" or "g[12]" — bracket-prefix at top, suffix (instance
    // path inside the generate block) optional. Group "label" carries the
    // generate block's loop-label, group "index" the iteration ordinal.
    private static readonly Regex GenerateNamePattern =
        new(@"^(?<label>[A-Za-z_][A-Za-z0-9_]*)\[(?<index>\d+)\](?<rest>\..*)?$", RegexOptions.Compiled);

    /// <summary>
    /// Group instances by their generate block label. Returns one
    /// <see cref="GenerateGroup"/> per detected label whose member count is at
    /// least 2 (single-instance generates are uninteresting visually). The
    /// member list within each group is sorted by ascending iteration index.
    /// </summary>
    public static IReadOnlyList<GenerateGroup> DetectGroups(IReadOnlyList<InstanceDecl> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        Dictionary<string, List<GenerateMember>> buckets = new(StringComparer.Ordinal);
        foreach (InstanceDecl inst in instances)
        {
            Match m = GenerateNamePattern.Match(inst.InstanceName);
            if (!m.Success) continue;
            string label = m.Groups["label"].Value;
            int idx = int.Parse(m.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!buckets.TryGetValue(label, out List<GenerateMember>? list))
            {
                list = [];
                buckets[label] = list;
            }
            list.Add(new GenerateMember(idx, inst));
        }

        List<GenerateGroup> groups = [];
        foreach ((string label, List<GenerateMember> members) in buckets)
        {
            if (members.Count < 2) continue;
            members.Sort((a, b) => a.Index.CompareTo(b.Index));
            groups.Add(new GenerateGroup(
                Label: label,
                LowIndex: members[0].Index,
                HighIndex: members[^1].Index,
                Members: members));
        }
        // Stable order by first-appearing label so callers get deterministic output.
        groups.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        return groups;
    }
}

/// <summary>
/// One unrolled cell of a generate block, paired with its iteration index.
/// </summary>
public sealed record GenerateMember(int Index, InstanceDecl Instance);

/// <summary>
/// A detected generate block — the loop label plus the iterations covered.
/// </summary>
public sealed record GenerateGroup(
    string Label,
    int LowIndex,
    int HighIndex,
    IReadOnlyList<GenerateMember> Members);
