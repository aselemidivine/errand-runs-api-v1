using ErrandRuns.Domain.Common;

namespace ErrandRuns.Domain.Errands;

public enum ErrandCategory
{
    Grocery,
    Laundry,
    Pharmacy,
    DocumentCollection,
    Custom,
    Other
}

public enum ErrandStatus
{
    Draft,
    PendingEstimate,
    PendingPayment,
    PaymentConfirmed,
    SearchingForRunner,
    RunnerAssigned,
    RunnerAccepted,
    RunnerEnRoute,
    AtStop,
    TaskInProgress,
    Delivering,
    AwaitingConfirmation,
    Completed,
    Cancelled,
    Disputed,
    Failed
}

public enum StopType
{
    Pickup,
    Shopping,
    Pharmacy,
    Laundry,
    Task,
    Delivery
}

public enum StopStatus
{
    Pending,
    Active,
    Completed,
    Skipped
}

public sealed record GeoPoint
{
    public decimal Latitude { get; }
    public decimal Longitude { get; }

    public GeoPoint(decimal latitude, decimal longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException("Invalid coordinates.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }
}

public sealed class ErrandStop
{
    private ErrandStop()
    {
        Address = string.Empty;
    }

    public ErrandStop(
        Guid id,
        int sequence,
        StopType type,
        string address,
        GeoPoint location,
        string? instructions)
    {
        Id = id;
        Sequence = sequence;
        Type = type;
        Address = string.IsNullOrWhiteSpace(address)
            ? throw new DomainException("Address is required.")
            : address.Trim();
        if (Address.Length > 500) throw new DomainException("Address cannot exceed 500 characters.");
        Location = location;
        Instructions = instructions;
    }

    public Guid Id { get; private set; }
    public int Sequence { get; private set; }
    public StopType Type { get; private set; }
    public string Address { get; private set; }
    public GeoPoint Location { get; private set; } = null!;
    public string? Instructions { get; private set; }
    public StopStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal void Start()
    {
        if (Status != StopStatus.Pending)
        {
            throw new DomainException("Only a pending stop can start.");
        }

        Status = StopStatus.Active;
    }

    internal void Complete(DateTimeOffset now)
    {
        if (Status != StopStatus.Active)
        {
            throw new DomainException("Only an active stop can complete.");
        }

        Status = StopStatus.Completed;
        CompletedAt = now;
    }
}

public sealed class ErrandItem
{
    private ErrandItem() { Name = string.Empty; Unit = string.Empty; }

    public ErrandItem(Guid id, string name, int quantity, string? unit = null, decimal? estimatedUnitPrice = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Item name is required.");
        if (name.Trim().Length > 160) throw new DomainException("Item name cannot exceed 160 characters.");
        if (quantity < 1) throw new DomainException("Item quantity must be at least one.");
        if (estimatedUnitPrice < 0) throw new DomainException("Estimated item price cannot be negative.");
        Id = id;
        Name = name.Trim();
        Quantity = quantity;
        Unit = string.IsNullOrWhiteSpace(unit) ? "item" : unit.Trim();
        EstimatedUnitPrice = estimatedUnitPrice;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int Quantity { get; private set; }
    public string Unit { get; private set; }
    public decimal? EstimatedUnitPrice { get; private set; }
}

public sealed class Errand
{
    private readonly List<ErrandStop> _stops = [];
    private readonly List<ErrandItem> _items = [];

    private Errand()
    {
        Title = string.Empty;
    }

    public Errand(
        Guid id,
        Guid customerId,
        string title,
        ErrandCategory category,
        DateTimeOffset? scheduledFor = null,
        string? preferredProvider = null,
        string? specialInstructions = null)
    {
        Id = id;
        CustomerId = customerId;
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new DomainException("Title is required.")
            : title.Trim();
        if (Title.Length > 160) throw new DomainException("Title cannot exceed 160 characters.");
        if (!Enum.IsDefined(category)) throw new DomainException("Errand category is invalid.");
        if (preferredProvider?.Trim().Length > 160) throw new DomainException("Preferred provider cannot exceed 160 characters.");
        if (specialInstructions?.Trim().Length > 1000) throw new DomainException("Special instructions cannot exceed 1000 characters.");
        Category = category;
        ScheduledFor = scheduledFor;
        PreferredProvider = Clean(preferredProvider);
        SpecialInstructions = Clean(specialInstructions);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? RunnerId { get; private set; }
    public string Title { get; private set; }
    public ErrandCategory Category { get; private set; }
    public ErrandStatus Status { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? PreferredProvider { get; private set; }
    public string? SpecialInstructions { get; private set; }
    public decimal MerchandiseEstimate { get; private set; }
    public decimal ServiceFee { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ErrandStop> Stops =>
        _stops.OrderBy(x => x.Sequence).ToList();

    public IReadOnlyList<ErrandItem> Items => _items.ToList();

    public decimal TotalEstimate => MerchandiseEstimate + ServiceFee;

    public void AddStop(ErrandStop stop)
    {
        if (Status != ErrandStatus.Draft)
        {
            throw new DomainException("Stops can only be changed in draft.");
        }

        if (_stops.Any(x => x.Sequence == stop.Sequence))
        {
            throw new DomainException("Stop sequence must be unique.");
        }

        _stops.Add(stop);
    }

    public void AddItem(ErrandItem item)
    {
        if (Status != ErrandStatus.Draft) throw new DomainException("Items can only be changed in draft.");
        if (_items.Count >= 100) throw new DomainException("An errand cannot contain more than 100 items.");
        _items.Add(item);
    }

    public void RequestEstimate()
    {
        if (_stops.Count == 0)
        {
            throw new DomainException(
                "An errand requires at least one stop.");
        }

        Transition(ErrandStatus.PendingEstimate);
    }

    public void SetEstimate(Money serviceFee, Money merchandiseEstimate)
    {
        if (serviceFee.Currency != merchandiseEstimate.Currency) throw new DomainException("Estimate currency mismatch.");
        Transition(ErrandStatus.PendingPayment);
        ServiceFee = serviceFee.Amount;
        MerchandiseEstimate = merchandiseEstimate.Amount;
        Currency = serviceFee.Currency;
    }

    public void SetEstimate() => SetEstimate(new Money(0), new Money(0));

    public void ConfirmPayment() =>
        Transition(ErrandStatus.PaymentConfirmed);

    public void BeginMatching() =>
        Transition(ErrandStatus.SearchingForRunner);

    public void AssignRunner(Guid runnerId)
    {
        Transition(ErrandStatus.RunnerAssigned);
        RunnerId = runnerId;
    }

    public void Accept(Guid runnerId)
    {
        if (RunnerId != runnerId)
        {
            throw new DomainException(
                "Only the assigned runner may accept.");
        }

        Transition(ErrandStatus.RunnerAccepted);
    }

    public void Decline(Guid runnerId)
    {
        EnsureRunner(runnerId);
        if (Status != ErrandStatus.RunnerAssigned) throw new DomainException("This assignment can no longer be declined.");
        RunnerId = null;
        Status = ErrandStatus.PaymentConfirmed;
    }

    public void StartJourney(Guid runnerId)
    {
        EnsureRunner(runnerId);
        Transition(ErrandStatus.RunnerEnRoute);
    }

    public void StartStop(Guid runnerId, Guid stopId)
    {
        EnsureRunner(runnerId);

        if (Status is not (
            ErrandStatus.RunnerEnRoute or
            ErrandStatus.TaskInProgress))
        {
            throw new DomainException("Cannot start a stop now.");
        }

        var stop = NextStop(stopId);

        stop.Start();
        Status = ErrandStatus.AtStop;
    }

    public void CompleteStop(
        Guid runnerId,
        Guid stopId,
        DateTimeOffset now)
    {
        EnsureRunner(runnerId);

        if (Status != ErrandStatus.AtStop)
        {
            throw new DomainException("No stop is active.");
        }

        var stop = _stops.SingleOrDefault(x => x.Id == stopId)
            ?? throw new DomainException("Stop not found.");

        stop.Complete(now);

        Status = _stops.All(x => x.Status == StopStatus.Completed)
            ? ErrandStatus.AwaitingConfirmation
            : ErrandStatus.TaskInProgress;
    }

    public void ConfirmCompletion(Guid customerId)
    {
        if (CustomerId != customerId)
        {
            throw new DomainException(
                "Only the customer may confirm completion.");
        }

        Transition(ErrandStatus.Completed);
    }

    public void Cancel(Guid actorId)
    {
        if (actorId != CustomerId && actorId != RunnerId)
        {
            throw new DomainException("Not permitted.");
        }

        if (Status is ErrandStatus.Completed or ErrandStatus.Cancelled)
        {
            throw new DomainException("Errand cannot be cancelled.");
        }

        Status = ErrandStatus.Cancelled;
    }

    private ErrandStop NextStop(Guid id)
    {
        var next = Stops.FirstOrDefault(
            x => x.Status == StopStatus.Pending)
            ?? throw new DomainException("No pending stops.");

        return next.Id == id
            ? next
            : throw new DomainException(
                "Stops must be completed in sequence.");
    }

    private void EnsureRunner(Guid id)
    {
        if (RunnerId != id)
        {
            throw new DomainException(
                "Only the assigned runner may perform this action.");
        }
    }

    private void Transition(ErrandStatus target)
    {
        var allowed = (Status, target) switch
        {
            (ErrandStatus.Draft, ErrandStatus.PendingEstimate) => true,
            (ErrandStatus.PendingEstimate, ErrandStatus.PendingPayment) => true,
            (ErrandStatus.PendingPayment, ErrandStatus.PaymentConfirmed) => true,
            (ErrandStatus.PaymentConfirmed, ErrandStatus.SearchingForRunner) => true,
            (ErrandStatus.SearchingForRunner, ErrandStatus.RunnerAssigned) => true,
            (ErrandStatus.RunnerAssigned, ErrandStatus.RunnerAccepted) => true,
            (ErrandStatus.RunnerAccepted, ErrandStatus.RunnerEnRoute) => true,
            (ErrandStatus.AwaitingConfirmation, ErrandStatus.Completed) => true,
            _ => false
        };

        if (!allowed)
        {
            throw new DomainException(
                $"Cannot transition from {Status} to {target}.");
        }

        Status = target;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
