using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("reactions")]
public class Reaction
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("sender_id")]
    public Guid? SenderId { get; set; }

    [Column("emoji")]
    [MaxLength(20)]
    [Required]
    public string Emoji { get; set; } = string.Empty;

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    // Navigation
    [ForeignKey("MessageId")]
    public Message Message { get; set; } = null!;

    [ForeignKey("SenderId")]
    public Contact? Sender { get; set; }
}
