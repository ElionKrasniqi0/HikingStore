using HikingStore.Enum;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Models
{
    public class Request
    {
        [Key]
        [Required]
        public Guid Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public string UserId { get; set; }
        [Required]
        [Display(Name = "Request Status")]
        public RequestStatus RequestStatus { get; set; } = RequestStatus.Recieved;
        [Required]
        [Display(Name = "Repeats")]
        public int RepeatedCount { get; set; } = 0;
    }
}