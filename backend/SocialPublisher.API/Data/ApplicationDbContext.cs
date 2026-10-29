using Microsoft.EntityFrameworkCore;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<SocialAccount> SocialAccounts { get; set; }
        public DbSet<SocialClient> SocialClients { get; set; }
        public DbSet<SocialConnection> SocialConnections { get; set; }
        public DbSet<SocialPost> SocialPosts { get; set; }
        public DbSet<SocialPostMedia> SocialPostMedia { get; set; }
        public DbSet<SocialPostPublish> SocialPostPublishes { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<ClientConnectionInvitation> ClientConnectionInvitations { get; set; }
        public DbSet<SocialPostApproval> SocialPostApprovals { get; set; }
        public DbSet<GoogleAdsAccount> GoogleAdsAccounts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<SocialAccount>()
                .HasOne(a => a.Client)
                .WithMany(c => c.Accounts)
                .HasForeignKey(a => a.ClientId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<SocialAccount>()
                .HasOne(a => a.Connection)
                .WithMany(c => c.Accounts)
                .HasForeignKey(a => a.ConnectionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<SocialConnection>()
                .HasOne(c => c.Client)
                .WithMany(cl => cl.Connections)
                .HasForeignKey(c => c.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SocialPost>()
                .HasOne(p => p.Client)
                .WithMany(c => c.Posts)
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<SocialClient>()
                .HasOne(sc => sc.Organization)
                .WithMany(o => o.Brands)
                .HasForeignKey(sc => sc.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(u => u.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);

            // ==========================================
            // PERFORMANCE INDEXES FOR SCALABILITY
            // ==========================================
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<SocialPost>()
                .HasIndex(p => p.ClientId);

            modelBuilder.Entity<SocialPost>()
                .HasIndex(p => p.Status);

            modelBuilder.Entity<SocialPost>()
                .HasIndex(p => p.ScheduledAt);

            modelBuilder.Entity<SocialPostPublish>()
                .HasIndex(pp => new { pp.PostId, pp.SocialAccountId });

            modelBuilder.Entity<SocialAccount>()
                .HasIndex(a => new { a.ClientId, a.Platform });

            modelBuilder.Entity<SocialConnection>()
                .HasIndex(c => new { c.ClientId, c.Provider });

            modelBuilder.Entity<ClientConnectionInvitation>()
                .HasIndex(i => i.Token)
                .IsUnique();

            // Google Ads Account → Client (brand workspace)
            modelBuilder.Entity<GoogleAdsAccount>()
                .HasOne(g => g.Client)
                .WithMany()
                .HasForeignKey(g => g.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GoogleAdsAccount>()
                .HasIndex(g => new { g.ClientId, g.CustomerId })
                .IsUnique();
        }
    }
}
