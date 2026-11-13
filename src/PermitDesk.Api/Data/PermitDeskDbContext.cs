using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Domain;

namespace PermitDesk.Api.Data;

public class PermitDeskDbContext : DbContext
{
    public PermitDeskDbContext(DbContextOptions<PermitDeskDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Parcel> Parcels => Set<Parcel>();
    public DbSet<WorkType> WorkTypes => Set<WorkType>();
    public DbSet<PermitApplication> Applications => Set<PermitApplication>();
    public DbSet<Transition> Transitions => Set<Transition>();
    public DbSet<ZoningCheck> ZoningChecks => Set<ZoningCheck>();
    public DbSet<ReviewSubmission> ReviewSubmissions => Set<ReviewSubmission>();
    public DbSet<CorrectionItem> CorrectionItems => Set<CorrectionItem>();
    public DbSet<CorrectionResponse> CorrectionResponses => Set<CorrectionResponse>();
    public DbSet<FeeQuote> FeeQuotes => Set<FeeQuote>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Permit> Permits => Set<Permit>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().ToTable("users");
        model.Entity<Parcel>().ToTable("parcels");
        model.Entity<WorkType>().ToTable("work_types").HasKey(w => w.Code);
        model.Entity<Transition>().ToTable("transitions");
        model.Entity<ZoningCheck>().ToTable("zoning_checks");
        model.Entity<ReviewSubmission>().ToTable("review_submissions");
        model.Entity<CorrectionItem>().ToTable("correction_items");
        model.Entity<CorrectionResponse>().ToTable("correction_responses");
        model.Entity<Permit>().ToTable("permits");
        model.Entity<Job>().ToTable("jobs");
        model.Entity<Inspection>().ToTable("inspections");
        model.Entity<Notification>().ToTable("notifications");

        model.Entity<PermitApplication>(e =>
        {
            e.ToTable("applications");
            e.HasOne(a => a.Applicant).WithMany().HasForeignKey(a => a.ApplicantUserId);
            e.HasOne(a => a.Parcel).WithMany().HasForeignKey(a => a.ParcelId);
        });

        model.Entity<FeeQuote>(e =>
        {
            e.ToTable("fee_quotes");
            e.HasOne(q => q.Application).WithMany().HasForeignKey(q => q.ApplicationId);
        });

        model.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasOne(p => p.Application).WithMany().HasForeignKey(p => p.ApplicationId);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // A new quote always means the application is waiting on payment.
        var newQuotes = ChangeTracker.Entries<FeeQuote>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        foreach (var quote in newQuotes)
        {
            var application = quote.Application ?? await Applications.FindAsync(new object[] { quote.ApplicationId }, cancellationToken);
            if (application != null)
            {
                application.State = ApplicationStates.FeesAssessed;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PermitApplication>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
