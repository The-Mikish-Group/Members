using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using System; // Make sure this is included for DateTime

namespace Members.Models
{
    public class UserProfile
    {
        [Key]
        [ForeignKey("User")] // Links this profile to a specific IdentityUser
        public required string UserId { get; set; }

        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public DateTime? Birthday { get; set; }
        public DateTime? Anniversary { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Plot { get; set; }
        public string? HomePhoneNumber { get; set; }
        public DateTime? LastLogin { get; set; }
        public required IdentityUser User { get; set; }
    }
}