namespace Erp.Core.Entities;

/// <summary>Row written by the SQL trigger (Source = Trigger) or by the API audit filter (Source = Api).</summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public string TableName { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Action { get; set; } = "";       // I / U / D
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedOn { get; set; }
    public string Source { get; set; } = "Api";
}

/// <summary>Transactional outbox row: an event saved in the same transaction as the business change.</summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredOn { get; set; }
    public DateTime? ProcessedOn { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>First response stored for an Idempotency-Key so a retried POST gets the same answer.</summary>
public class IdempotencyRecord
{
    public string IdempotencyKey { get; set; } = "";
    public string RequestPath { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedOn { get; set; }
}
