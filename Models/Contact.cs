using System.ComponentModel.DataAnnotations;

public class Contact
{
    [Key]
    public int CoId { get; set; }
    [Required(ErrorMessage = "This field must be completed")]
    public string? Name { get; set; }
    [Required(ErrorMessage = "This field must be completed")]
    [EmailAddress]
    public string? Email { get; set; }
    public string? Subject { get; set; }
    public string? Message { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}