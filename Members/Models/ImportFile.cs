using System.ComponentModel.DataAnnotations;

namespace Members.Models // Replace with your actual namespace
{
    public class ImportFile
    {             
        [Key]
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? AddressLine1 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Plot { get; set; }
        public string? PhoneNumber { get; set; }
        public string? HomePhoneNumber { get; set; }

        // Add any other properties that match your table columns
    }
}