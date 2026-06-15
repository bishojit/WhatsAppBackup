using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("chats")]
public class Chat
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("jid")]
    [MaxLength(100)]
    [Required]
    public string Jid { get; set; } = string.Empty;

    [Column("chat_type")]
    [MaxLength(20)]
    [Required]
    public string ChatType { get; set; } = "dm"; // 'dm' or 'group'

    [Column("name")]
    [MaxLength(255)]
    public string? Name { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_archived")]
    public bool IsArchived { get; set; } = false;

    [Column("is_muted")]
    public bool IsMuted { get; set; } = false;

    [Column("muted_until")]
    public DateTime? MutedUntil { get; set; }

    [Column("unread_count")]
    public int UnreadCount { get; set; } = 0;

    [Column("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<GroupParticipant> Participants { get; set; } = new List<GroupParticipant>();
}
