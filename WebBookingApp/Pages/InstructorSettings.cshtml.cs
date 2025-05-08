using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;  // Import the configuration namespace

using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;
using System;

namespace WebBookingApp.Pages
{
    public class InstructorSettingsModel : PageModel
    {
        private readonly ILogger<InstructorSettingsModel> _logger;
        private readonly PasswordHasher<InstructorSettingsModel> _passwordHasher = new();
        private readonly string connString;

        // Instructor properties
        [BindProperty] public int ProfessorId { get; set; }
        [BindProperty, Required] public string FirstName { get; set; } = "";
        [BindProperty] public string MiddleName { get; set; } = "";
        [BindProperty, Required] public string LastName { get; set; } = "";
        [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
        [BindProperty, Required] public string Program { get; set; } = "";
        [BindProperty] public string ContactNumber { get; set; } = "";
        [BindProperty] public string Gender { get; set; } = "";
        [BindProperty, DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
        [BindProperty] public string Address { get; set; } = "";
        [BindProperty] public string City { get; set; } = "";
        [BindProperty, DataType(DataType.Date)] public DateTime? JoiningDate { get; set; }
        [BindProperty] public string EmploymentStatus { get; set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        [TempData] public string Message { get; set; } = "";

        [BindProperty, DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }

        [BindProperty, DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, and special character")]
        public string? NewPassword { get; set; }

        [BindProperty, DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string? ConfirmPassword { get; set; }

        public InstructorSettingsModel(ILogger<InstructorSettingsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            connString = configuration.GetConnectionString("DefaultConnection");  // Get the connection string from configuration
        }

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("User not authenticated");
                return;
            }

            using SqlConnection conn = new(connString);
            conn.Open();

            string query = @"
                SELECT professor_id, first_name, middle_name, last_name, 
                       email, program, contact_number, gender, 
                       date_of_birth, address, city, joining_date, 
                       employment_status, role
                FROM dbo.userCICT
                WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    ProfessorId = Convert.ToInt32(reader["professor_id"]);
                    FirstName = reader["first_name"].ToString();
                    MiddleName = reader["middle_name"]?.ToString() ?? "";
                    LastName = reader["last_name"].ToString();
                    Email = reader["email"].ToString();
                    Program = reader["program"].ToString();
                    ContactNumber = reader["contact_number"]?.ToString() ?? "";
                    Gender = reader["gender"]?.ToString() ?? "";
                    DateOfBirth = reader["date_of_birth"] != DBNull.Value ? (DateTime?)reader["date_of_birth"] : null;
                    Address = reader["address"]?.ToString() ?? "";
                    City = reader["city"]?.ToString() ?? "";
                    JoiningDate = reader["joining_date"] != DBNull.Value ? (DateTime?)reader["joining_date"] : null;
                    EmploymentStatus = reader["employment_status"]?.ToString() ?? "";
                    Role = reader["role"].ToString();
                }
            }

            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                }
            }
        }

        public async Task<IActionResult> OnPostUpdateSettingsAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return RedirectToPage("/Login");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                // Only validate current password if changing password
                if (!string.IsNullOrEmpty(NewPassword))
                {
                    string verifyQuery = "SELECT password FROM dbo.userCICT WHERE email = @Email";
                    string currentDbPassword = "";

                    using (SqlCommand verifyCmd = new(verifyQuery, conn))
                    {
                        verifyCmd.Parameters.AddWithValue("@Email", userEmail);
                        var result = await verifyCmd.ExecuteScalarAsync();
                        currentDbPassword = result?.ToString() ?? "";
                    }

                    var verifyResult = _passwordHasher.VerifyHashedPassword(this, currentDbPassword, CurrentPassword);
                    if (verifyResult == PasswordVerificationResult.Failed)
                    {
                        ModelState.AddModelError("CurrentPassword", "Current password is incorrect");
                        return Page();
                    }
                }

                string query = @"UPDATE dbo.userCICT 
                        SET email = @Email,
                            contact_number = @ContactNumber,
                            gender = @Gender,
                            date_of_birth = @DateOfBirth,
                            address = @Address,
                            city = @City,
                            employment_status = @EmploymentStatus";

                if (!string.IsNullOrEmpty(NewPassword))
                {
                    query += ", password = @NewPassword";
                }

                query += " WHERE email = @CurrentEmail";

                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", Email);
                cmd.Parameters.AddWithValue("@ContactNumber", string.IsNullOrEmpty(ContactNumber) ? (object)DBNull.Value : ContactNumber);
                cmd.Parameters.AddWithValue("@Gender", string.IsNullOrEmpty(Gender) ? (object)DBNull.Value : Gender);
                cmd.Parameters.AddWithValue("@DateOfBirth", DateOfBirth.HasValue ? (object)DateOfBirth.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(Address) ? (object)DBNull.Value : Address);
                cmd.Parameters.AddWithValue("@City", string.IsNullOrEmpty(City) ? (object)DBNull.Value : City);
                cmd.Parameters.AddWithValue("@EmploymentStatus", string.IsNullOrEmpty(EmploymentStatus) ? (object)DBNull.Value : EmploymentStatus);
                cmd.Parameters.AddWithValue("@CurrentEmail", userEmail);

                if (!string.IsNullOrEmpty(NewPassword))
                {
                    string hashedPassword = _passwordHasher.HashPassword(this, NewPassword);
                    cmd.Parameters.AddWithValue("@NewPassword", hashedPassword);
                }

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected == 0)
                {
                    ModelState.AddModelError("", "Failed to update settings. No records were modified.");
                    return Page();
                }

                if (Email != userEmail)
                {
                    string updatePictureQuery = "UPDATE dbo.userPictures SET email = @NewEmail WHERE email = @OldEmail";
                    using SqlCommand updateEmailCmd = new(updatePictureQuery, conn);
                    updateEmailCmd.Parameters.AddWithValue("@NewEmail", Email);
                    updateEmailCmd.Parameters.AddWithValue("@OldEmail", userEmail);
                    await updateEmailCmd.ExecuteNonQueryAsync();

                    var identity = new ClaimsIdentity(User.Identity);
                    identity.RemoveClaim(identity.FindFirst(ClaimTypes.Email));
                    identity.AddClaim(new Claim(ClaimTypes.Email, Email));
                    await HttpContext.SignOutAsync();
                    await HttpContext.SignInAsync(new ClaimsPrincipal(identity));
                }

                TempData["Message"] = "Settings updated successfully!";
                return RedirectToPage("/InstructorSettings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings");
                ModelState.AddModelError("", "Error updating settings. Please try again.");
                return Page();
            }
        }

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            using SqlConnection conn = new(connString);
            conn.Open();

            byte[]? profileImage = null;
            string query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    profileImage = (byte[])reader["Picture"];
                }
            }

            return profileImage != null
                ? File(profileImage, "image/jpeg", "profile.jpg")
                : File("/images/image.png", "image/png");
        }

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                TempData["Message"] = "Please select a file.";
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                TempData["Message"] = "Only image files (.jpg, .png, .gif) are allowed.";
                return Page();
            }

            try
            {
                byte[] fileData;
                using (var memoryStream = new MemoryStream())
                {
                    await UploadedFile.CopyToAsync(memoryStream);
                    fileData = memoryStream.ToArray();
                }

                string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrEmpty(userEmail)) return Page();

                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                string query = @"
                    MERGE INTO dbo.userPictures AS target
                    USING (SELECT @Email AS Email) AS source
                    ON target.email = source.Email
                    WHEN MATCHED THEN 
                        UPDATE SET Picture = @ImageData
                    WHEN NOT MATCHED THEN 
                        INSERT (email, Picture) VALUES (@Email, @ImageData);";

                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", userEmail);
                cmd.Parameters.AddWithValue("@ImageData", fileData);
                await cmd.ExecuteNonQueryAsync();

                return RedirectToPage("/InstructorSettings");
            }
            catch
            {
                TempData["Message"] = "Error uploading file.";
                return Page();
            }
        }

        public async Task<IActionResult> OnPostLogout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToPage("/Login");
        }
    }
}