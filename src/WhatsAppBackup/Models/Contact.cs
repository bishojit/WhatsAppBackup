using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("contacts")]
public class Contact
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("phone")]
    [MaxLength(20)]
    [Required]
    public string Phone { get; set; } = string.Empty;

    [Column("push_name")]
    [MaxLength(255)]
    public string? PushName { get; set; }

    [Column("saved_name")]
    [MaxLength(255)]
    public string? SavedName { get; set; }

    [Column("about")]
    public string? About { get; set; }

    [Column("profile_pic_url")]
    public string? ProfilePicUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<GroupParticipant> GroupMemberships { get; set; } = new List<GroupParticipant>();
    public ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();
}
