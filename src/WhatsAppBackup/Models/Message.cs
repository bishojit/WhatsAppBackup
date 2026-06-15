using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("messages")]
public class Message
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("message_id")]
    [MaxLength(100)]
    [Required]
    public string MessageId { get; set; } = string.Empty;

    [Column("chat_id")]
    public Guid ChatId { get; set; }

    [Column("sender_id")]
    public Guid? SenderId { get; set; }

    [Column("message_type")]
    [MaxLength(30)]
    [Required]
    public string MessageType { get; set; } = "text";

    [Column("body")]
    public string? Body { get; set; }

    [Column("caption")]
    public string? Caption { get; set; }

    [Column("is_from_me")]
    public bool IsFromMe { get; set; } = false;

    [Column("is_forwarded")]
    public bool IsForwarded { get; set; } = false;

    [Column("forward_score")]
    public int? ForwardScore { get; set; }

    [Column("reply_to_id")]
    public Guid? ReplyToId { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "sent";

    [Column("starred")]
    public bool Starred { get; set; } = false;

    [Column("deleted")]
    public bool Deleted { get; set; } = false;

    [Column("edited")]
    public bool Edited { get; set; } = false;

    [Column("timestamp")]
    [Required]
    public DateTime Timestamp { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("ChatId")]
    public Chat Chat { get; set; } = null!;

    [ForeignKey("SenderId")]
    public Contact? Sender { get; set; }

    [ForeignKey("ReplyToId")]
    public Message? ReplyTo { get; set; }

    public ICollection<Media> MediaItems { get; set; } = new List<Media>();
    public ICollection<Reaction> Reactions { get; set; } = new List<Reaction>();
    public Poll? Poll { get; set; }
}
