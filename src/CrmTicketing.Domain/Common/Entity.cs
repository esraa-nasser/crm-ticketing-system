namespace CrmTicketing.Domain.Common;

/// <summary>
/// Base type for aggregate roots and entities. Identity is the only thing this
/// base class asserts; behaviour and invariants belong on the derived type.
/// </summary>
/// <remarks>
/// Domain entities in this solution follow three rules (see docs/constitution.md):
/// no persistence attributes, no DTO leakage, and no public setters that would
/// let a caller bypass an invariant.
/// </remarks>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
