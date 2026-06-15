using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppBackup.Models;

[Table("polls")]
public class Poll
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("question")]
    [Required]
    public string Question { get; set; } = string.Empty;

    [Column("allows_multiple")]
    public bool AllowsMultiple { get; set; } = false;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("MessageId")]
    public Message Message { get; set; } = null!;

    public ICollection<PollOption> Options { get; set; } = new List<PollOption>();
}

[Table("poll_options")]
public class PollOption
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("poll_id")]
    public Guid PollId { get; set; }

    [Column("option_text")]
    [Required]
    public string OptionText { get; set; } = string.Empty;

    [Column("option_index")]
    public int OptionIndex { get; set; }

    // Navigation
    [ForeignKey("PollId")]
    public Poll Poll { get; set; } = null!;

    public ICollection<PollVote> Votes { get; set; } = new List<PollVote>();
}

[Table("poll_votes")]
public class PollVote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("poll_id")]
    public Guid PollId { get; set; }

    [Column("option_id")]
    public Guid OptionId { get; set; }

    [Column("voter_id")]
    public Guid? VoterId { get; set; }

    [Column("voted_at")]
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("PollId")]
    public Poll Poll { get; set; } = null!;

    [ForeignKey("OptionId")]
    public PollOption Option { get; set; } = null!;

    [ForeignKey("VoterId")]
    public Contact? Voter { get; set; }
}
