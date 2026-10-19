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
        }
    }
}
