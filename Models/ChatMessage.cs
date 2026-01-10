using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ChatRoomId { get; set; }

        [ForeignKey("ChatRoomId")]
        public virtual ChatRoom? ChatRoom { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;

        [ForeignKey("SenderId")]
        public virtual Microsoft.AspNetCore.Identity.IdentityUser? Sender { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public bool IsAgentMessage { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }
    }
}
