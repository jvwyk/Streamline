namespace Streamline.Domain.Registry;

/// <summary>
/// Pure-function gate over <see cref="RegistryEntry"/> consistency.
/// Aggregates the lazy-violation lists from <see cref="RegistryEntry"/>
/// and the per-column lazy-violation lists from
/// <see cref="Streamline.Core.ValueTypes.ColumnDefinition"/>, plus
/// adds cross-column rules (PK columns must be required; target
/// column names unique) and — when validating a set of entries —
/// cross-entry rules (DependsOn references must exist; no cycles).
/// </summary>
/// <remarks>
/// <para>
/// <b>No short-circuit.</b> Returns every violation found across
/// every rule and every entry, so registry editors see all problems
/// in one round-trip. Per the lazy-violation pattern from sub-phase
/// 1a, each contributor (RegistryEntry, ColumnDefinition, this
/// validator) yields strings; <see cref="Validate(RegistryEntry)"/>
/// concatenates them. <see cref="Validate(IEnumerable{RegistryEntry})"/>
/// repeats the per-entry pass and adds cross-entry rules at the end.
/// </para>
/// <para>
/// <b>No I/O, no observations.</b> Same discipline as the row state
/// machine: pure logic, single source of truth for "is this
/// registry entry coherent?". Sub-phase 1g decides where the call
/// sites are (load time, ingestion startup, both).
/// </para>
/// </remarks>
public static class RegistryValidator
{
    /// <summary>
    /// Validate a single entry. Returns every violation found —
    /// per-entry invariants, per-column invariants, and the
    /// cross-column rules this validator owns.
    /// </summary>
    public static IEnumerable<string> Validate(RegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Entry-level invariants (1c).
        foreach (var violation in entry.DescribeInvariantViolations())
        {
            yield return violation;
        }

        // Column-level invariants (1a).
        foreach (var column in entry.Schema.Columns)
        {
            foreach (var violation in column.DescribeInvariantViolations())
            {
                // Prefix with the table name so multi-entry
                // aggregation can attribute the violation.
                yield return $"{entry.TableName}.{violation}";
            }
        }

        // Cross-column rules.
        foreach (var violation in CheckPrimaryKeyColumnsAreRequired(entry))
        {
            yield return violation;
        }

        foreach (var violation in CheckTargetColumnNamesAreUnique(entry))
        {
            yield return violation;
        }
    }

    /// <summary>
    /// Validate a set of entries. Runs <see cref="Validate(RegistryEntry)"/>
    /// against every entry, then adds cross-entry rules: every
    /// <c>DependsOn</c> reference must point to a table in the set,
    /// and the dependency graph must be acyclic.
    /// </summary>
    public static IEnumerable<string> Validate(IEnumerable<RegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var entryList = entries.ToArray();

        foreach (var entry in entryList)
        {
            foreach (var violation in Validate(entry))
            {
                yield return violation;
            }
        }

        foreach (var violation in CheckDependsOnReferencesExist(entryList))
        {
            yield return violation;
        }

        foreach (var violation in CheckNoCyclicDependencies(entryList))
        {
            yield return violation;
        }
    }

    // ---- cross-column rules ---------------------------------------------

    private static IEnumerable<string> CheckPrimaryKeyColumnsAreRequired(RegistryEntry entry)
    {
        foreach (var column in entry.Schema.PrimaryKey)
        {
            if (!column.IsRequired)
            {
                yield return $"{entry.TableName}.{column.Name}: primary-key columns must be IsRequired=true.";
            }
        }
    }

    private static IEnumerable<string> CheckTargetColumnNamesAreUnique(RegistryEntry entry)
    {
        var targetGroups = entry.Schema.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.TargetColumn))
            .GroupBy(c => c.TargetColumn!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in targetGroups)
        {
            var sources = string.Join(", ", group.Select(c => c.Name));
            yield return $"{entry.TableName}: target column '{group.Key}' is mapped from multiple source columns ({sources}).";
        }
    }

    // ---- cross-entry rules ----------------------------------------------

    private static IEnumerable<string> CheckDependsOnReferencesExist(IReadOnlyList<RegistryEntry> entries)
    {
        var known = new HashSet<string>(
            entries.Select(e => e.TableName), StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            foreach (var dep in entry.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(dep))
                {
                    // Already reported by RegistryEntry.DescribeInvariantViolations.
                    continue;
                }
                if (!known.Contains(dep))
                {
                    yield return $"{entry.TableName}: DependsOn references '{dep}' which is not a known registry entry.";
                }
            }
        }
    }

    private static List<string> CheckNoCyclicDependencies(IReadOnlyList<RegistryEntry> entries)
    {
        // Classic DFS cycle detection. White = unvisited, gray = on the
        // current DFS stack, black = fully explored. A back-edge to a
        // gray node identifies a cycle.
        var entriesByName = entries
            .GroupBy(e => e.TableName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var color = new Dictionary<string, NodeColor>(StringComparer.Ordinal);
        var reportedCycles = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var name in entriesByName.Keys)
        {
            color[name] = NodeColor.White;
        }

        foreach (var name in entriesByName.Keys)
        {
            if (color[name] == NodeColor.White)
            {
                Visit(name, []);
            }
        }

        return violations;

        void Visit(string name, List<string> stack)
        {
            color[name] = NodeColor.Gray;
            stack.Add(name);

            if (entriesByName.TryGetValue(name, out var entry))
            {
                foreach (var dep in entry.DependsOn)
                {
                    if (string.IsNullOrWhiteSpace(dep)
                        || !entriesByName.ContainsKey(dep)
                        || string.Equals(dep, name, StringComparison.Ordinal))
                    {
                        // Blank entries, unknown references, and
                        // self-loops are all reported elsewhere (by
                        // RegistryEntry.DescribeInvariantViolations and
                        // CheckDependsOnReferencesExist). Skip here to
                        // avoid duplicate reporting.
                        continue;
                    }

                    if (color.TryGetValue(dep, out var depColor))
                    {
                        if (depColor == NodeColor.Gray)
                        {
                            // Back-edge: cycle. Report the cycle path.
                            var cycleStart = stack.IndexOf(dep);
                            var cycle = stack.Skip(cycleStart).Append(dep).ToList();
                            var key = string.Join("->", cycle.OrderBy(x => x, StringComparer.Ordinal));
                            if (reportedCycles.Add(key))
                            {
                                violations.Add(
                                    $"Circular DependsOn dependency: {string.Join(" -> ", cycle)}.");
                            }
                        }
                        else if (depColor == NodeColor.White)
                        {
                            Visit(dep, stack);
                        }
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            color[name] = NodeColor.Black;
        }
    }

    private enum NodeColor { White, Gray, Black }
}
