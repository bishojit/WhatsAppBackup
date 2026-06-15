using Microsoft.EntityFrameworkCore;
using WhatsAppBackup.Models;

namespace WhatsAppBackup.Data;

public class WhatsAppDbContext : DbContext
{
    public WhatsAppDbContext(DbContextOptions<WhatsAppDbContext> options) : base(options)
    {
    }

    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<GroupParticipant> GroupParticipants => Set<GroupParticipant>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Contacts
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasIndex(e => e.Phone).IsUnique();
        });

        // Chats
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasIndex(e => e.Jid).IsUnique();
            entity.HasIndex(e => e.LastMessageAt);
        });

        // Messages
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => e.ChatId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SenderId);
            entity.HasIndex(e => e.MessageType);

            entity.HasOne(e => e.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Sender)
                .WithMany(c => c.SentMessages)
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ReplyTo)
                .WithMany()
                .HasForeignKey(e => e.ReplyToId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Media
        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasIndex(e => e.MessageId);

            entity.HasOne(e => e.Message)
                .WithMany(m => m.MediaItems)
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Group Participants
        modelBuilder.Entity<GroupParticipant>(entity =>
        {
            entity.HasIndex(e => new { e.ChatId, e.ContactId }).IsUnique();

            entity.HasOne(e => e.Chat)
                .WithMany(c => c.Participants)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Contact)
                .WithMany(c => c.GroupMemberships)
                .HasForeignKey(e => e.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Reactions
        modelBuilder.Entity<Reaction>(entity =>
        {
            entity.HasIndex(e => new { e.MessageId, e.SenderId }).IsUnique();

            entity.HasOne(e => e.Message)
                .WithMany(m => m.Reactions)
                .HasForeignKey(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Sender)
                .WithMany(c => c.Reactions)
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Polls
        modelBuilder.Entity<Poll>(entity =>
        {
            entity.HasOne(e => e.Message)
                .WithOne(m => m.Poll)
                .HasForeignKey<Poll>(e => e.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollOption>(entity =>
        {
            entity.HasOne(e => e.Poll)
                .WithMany(p => p.Options)
                .HasForeignKey(e => e.PollId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollVote>(entity =>
        {
            entity.HasOne(e => e.Option)
                .WithMany(o => o.Votes)
                .HasForeignKey(e => e.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
