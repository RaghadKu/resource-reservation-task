using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Domain.Entities;

public class Resource : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Capacity { get; private set; }
    public bool IsActive { get; private set; }

    private Resource() { } 

    public static Resource Create(string name, string? description, int capacity, DateTime now)
    {
        var resource = new Resource { IsActive = true, CreatedAt = now, UpdatedAt = now };
        resource.SetDetails(name, description, capacity);
        return resource;
    }

    public void Update(string name, string? description, int capacity, DateTime now)
    {
        SetDetails(name, description, capacity);
        UpdatedAt = now;
    }

    public void Deactivate(DateTime now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTime now)
    {
        if (IsActive) return;
        IsActive = true;
        UpdatedAt = now;
    }

    private void SetDetails(string name, string? description, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name is required.");
        if (name.Trim().Length > FieldLengths.ResourceName)
            throw new DomainValidationException($"Name cannot exceed {FieldLengths.ResourceName} characters.");
        if (description is { Length: > FieldLengths.ResourceDescription })
            throw new DomainValidationException($"Description cannot exceed {FieldLengths.ResourceDescription} characters.");
        if (capacity <= 0)
            throw new DomainValidationException("Capacity must be greater than zero.");

        Name = name.Trim();
        Description = description?.Trim();
        Capacity = capacity;
    }
}
