using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Members.Models // This is crucial! Make sure it matches your Models folder namespace
{
    public class Plots    {
        [Key]
        [ForeignKey("User")] // Links this profile to a specific IdentityUser
        public required string UserId { get; set; }        
        public string? Plot { get; set; }
        public required IdentityUser User { get; set; } 
    }
}