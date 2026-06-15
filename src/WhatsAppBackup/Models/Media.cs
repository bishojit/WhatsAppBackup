using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("media")]
public class Media
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("media_type")]
    [MaxLength(30)]
    [Required]
    public string MediaType { get; set; } = string.Empty;

    [Column("mime_type")]
    [MaxLength(100)]
    public string? MimeType { get; set; }

    [Column("file_size")]
    public long? FileSize { get; set; }

    [Column("file_path")]
    public string? FilePath { get; set; }

    [Column("original_url")]
    public string? OriginalUrl { get; set; }

    [Column("file_name")]
    [MaxLength(255)]
    public string? FileName { get; set; }

    [Column("width")]
    public int? Width { get; set; }

    [Column("height")]
    public int? Height { get; set; }

    [Column("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [Column("thumbnail_path")]
    public string? ThumbnailPath { get; set; }

    [Column("sha256_hash")]
    [MaxLength(64)]
    public string? Sha256Hash { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("MessageId")]
    public Message Message { get; set; } = null!;
}
