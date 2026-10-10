using HikingStore.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class ChatRoom
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual HikingStore.Data.ApplicationUser? User { get; set; }

        public string? AgentId { get; set; }

        [ForeignKey("AgentId")]
        public virtual HikingStore.Data.ApplicationUser? Agent { get; set; }

        [Required]
        public string Subject { get; set; } = "General Inquiry";

        public ChatRoomStatus Status { get; set; } = ChatRoomStatus.Open;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }

        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public enum ChatRoomStatus
    {
        Open = 0,
        InProgress = 1,
        Resolved = 2,
        Closed = 3
    }
}
