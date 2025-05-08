using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;
using System;

namespace WebBookingApp.Pages
{
    public class StudentSettingsModel : PageModel
    {
        private readonly ILogger<StudentSettingsModel> _logger;
        private readonly string connString;

        private readonly PasswordHasher<StudentSettingsModel> _passwordHasher = new();

        // Student properties
        [BindProperty] public string StudentId { get; set; } = "";
        [BindProperty, Required] public string FirstName { get; set; } = "";
        [BindProperty] public string MiddleName { get; set; } = "";
        [BindProperty, Required] public string LastName { get; set; } = "";
        [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
        [BindProperty, Required] public string Program { get; set; } = "";
        [BindProperty, Required] public string MobileNumber { get; set; } = "";
        [BindProperty] public string YearLevel { get; set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";

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

        private readonly IConfiguration _configuration;

        public StudentSettingsModel(ILogger<StudentSettingsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            connString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection string not configured.");
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
                SELECT student_id, first_name, middle_name, last_name, 
                       email, program, mobile_number, YearLevel, role
                FROM dbo.userStudents
                WHERE email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    StudentId = reader["student_id"].ToString();
                    FirstName = reader["first_name"].ToString();
                    MiddleName = reader["middle_name"]?.ToString() ?? "";
                    LastName = reader["last_name"].ToString();
                    Email = reader["email"].ToString();
                    Program = reader["program"].ToString();
                    MobileNumber = reader["mobile_number"].ToString();
                    YearLevel = reader["YearLevel"]?.ToString() ?? "";
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
                    string verifyQuery = "SELECT password FROM dbo.userStudents WHERE email = @Email";
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

                string query = @"UPDATE dbo.userStudents 
                        SET email = @Email,
                            mobile_number = @MobileNumber,
                            YearLevel = @YearLevel";

                if (!string.IsNullOrEmpty(NewPassword))
                {
                    query += ", password = @NewPassword";
                }

                query += " WHERE email = @CurrentEmail";

                using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Email", Email);
                cmd.Parameters.AddWithValue("@MobileNumber", MobileNumber);
                cmd.Parameters.AddWithValue("@YearLevel", string.IsNullOrEmpty(YearLevel) ? (object)DBNull.Value : YearLevel);
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
                return RedirectToPage("/StudentSettings");
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

                return RedirectToPage("/StudentSettings");
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
