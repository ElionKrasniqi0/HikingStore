using System.ComponentModel.DataAnnotations;

namespace HikingStore.Models
{
    public class NewsletterSubscriber
    {
        [Key]
        public int Id { get; set; }

        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
