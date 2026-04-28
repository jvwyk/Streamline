namespace Streamline.Core.Enums;

/// <summary>
/// The kind of transformer the engine dispatches to. Selected by the
/// registry entry's <c>transform.kind</c> field. Two kinds in v1; new
/// kinds are added only when a real job needs one (per plan §11
/// decision 8).
/// </summary>
public enum TransformKind
{
    /// <summary>
    /// A registered Postgres function. The engine calls it with
    /// <c>p_batch_id TEXT</c> and reads the function's typed return
    /// row for outcome counts. The function owns the destination
    /// write and the lineage population.
    /// </summary>
    SqlFunction,

    /// <summary>
    /// A C# class implementing <c>ITransformer</c>, resolved via DI
    /// by fully-qualified type name. Receives a <c>BatchContext</c>
    /// with an <c>ILineageWriter</c> and returns a
    /// <c>TransformOutcome</c>.
    /// </summary>
    CSharp,
}
