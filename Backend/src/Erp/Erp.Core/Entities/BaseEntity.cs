namespace Erp.Core.Entities;

/*  Interface segregation (the "I" in SOLID): small interfaces, each one
    capability. A ledger row is only creation-audited; a master record is
    fully audited and soft-deletable. The EF interceptor checks these
    interfaces, not concrete classes.                                       */

/// <summary>Who created the row and when.</summary>
public interface ICreationAudited
{
    string CreatedBy { get; set; }
    DateTime CreatedOn { get; set; }
}

/// <summary>Creation + last modification.</summary>
public interface IAuditable : ICreationAudited
{
    string? ModifiedBy { get; set; }
    DateTime? ModifiedOn { get; set; }
}

/// <summary>Rows are flagged, never physically deleted.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}

/// <summary>
/// Abstract base class ("is-a" + shared code): every master/transaction entity
/// IS a BaseEntity and inherits Id + audit + soft-delete. You can't create a
/// BaseEntity on its own. Compare with the interfaces above ("can-do"):
/// a class can implement many interfaces but inherit only one class.
/// </summary>
public abstract class BaseEntity : IAuditable, ISoftDelete
{
    public int Id { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Run-time polymorphism: derived classes override this to describe themselves.</summary>
    public virtual string DisplayName => $"{GetType().Name} #{Id}";

    public override string ToString() => DisplayName;
}
