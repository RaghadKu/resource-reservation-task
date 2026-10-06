namespace ResourceReservation.Application.Waitlist;

public sealed class WaitlistOptions
{
    public const string SectionName = "Waitlist";

    public int OfferWindowMinutes { get; set; } = 15;
}
