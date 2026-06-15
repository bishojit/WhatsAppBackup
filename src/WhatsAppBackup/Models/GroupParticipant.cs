using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("group_participants")]
public class GroupParticipant
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("chat_id")]
    public Guid ChatId { get; set; }

    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("role")]
    [MaxLength(20)]
    public string Role { get; set; } = "member";

    [Column("joined_at")]
    public DateTime? JoinedAt { get; set; }

    [Column("added_by")]
    public Guid? AddedById { get; set; }

    // Navigation
    [ForeignKey("ChatId")]
    public Chat Chat { get; set; } = null!;

    [ForeignKey("ContactId")]
    public Contact Contact { get; set; } = null!;

    [ForeignKey("AddedById")]
    public Contact? AddedBy { get; set; }
}
