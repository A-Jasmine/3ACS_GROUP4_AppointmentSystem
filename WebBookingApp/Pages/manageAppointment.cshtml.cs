using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;

namespace WebBookingApp.Pages
{
    public class manageAppointmentModel : PageModel
    {
        private readonly ILogger<manageAppointmentModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public byte[]? ProfilePicture { get; set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; set; } = string.Empty;

        public manageAppointmentModel(ILogger<manageAppointmentModel> logger) => _logger = logger;

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

            // ✅ Fetch FirstName & LastName from dbo.userCICT
            string query = "SELECT first_name, last_name FROM dbo.userCICT WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    _logger.LogInformation($"Retrieved Name: {FirstName} {LastName}");
                }
            }

            // ✅ Fetch Profile Picture from dbo.userPictures
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read() && reader["Picture"] != DBNull.Value)
                {
                    ProfilePicture = (byte[])reader["Picture"];
                    _logger.LogInformation("Profile picture retrieved successfully.");
                }
                else
                {
                    _logger.LogWarning("No profile picture found.");
                }
            }
        }

        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            byte[]? profileImage = null;

            using SqlConnection conn = new(connString);
            conn.Open();

            // ✅ Fetch Profile Picture from dbo.userPictures
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

            if (profileImage != null)
            {
                Response.Headers["Content-Disposition"] = "inline; filename=profile.jpg";
                return File(profileImage, "image/jpeg");
            }

            return File("/images/image.png", "image/png"); // Fallback image
        }

        public async Task<IActionResult> OnPostUploadPictureAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
            {
                Message = "Please select a file.";
                _logger.LogWarning("No file uploaded.");
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
                _logger.LogWarning("Invalid file format: " + fileExtension);
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
                if (string.IsNullOrEmpty(userEmail))
                {
                    Message = "User not authenticated.";
                    return Page();
                }

                using SqlConnection conn = new(connString);
                await conn.OpenAsync();

                // ✅ Check if user is a professor
                bool isProfessor = false;
                string checkProfessorQuery = "SELECT COUNT(*) FROM dbo.userCICT WHERE email = @Email";
                using (SqlCommand checkCmd = new(checkProfessorQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Email", userEmail);
                    isProfessor = (int)await checkCmd.ExecuteScalarAsync() > 0;
                }

                // ✅ Update or Insert Profile Picture and isProfessor flag
                string query = @"
                MERGE INTO dbo.userPictures AS target
                USING (SELECT @Email AS Email) AS source
                ON target.email = source.Email
                WHEN MATCHED THEN 
                    UPDATE SET Picture = @ImageData, isProfessor = @IsProfessor
                WHEN NOT MATCHED THEN 
                    INSERT (email, Picture, isProfessor) VALUES (@Email, @ImageData, @IsProfessor);";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
                    cmd.Parameters.AddWithValue("@IsProfessor", isProfessor);
                    await cmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Profile picture uploaded successfully.");
                return RedirectToPage("/dashboardProfessor");
            }
            catch (Exception ex)
            {
                Message = "Error uploading file: " + ex.Message;
                _logger.LogError("Error uploading profile picture: " + ex.Message);
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
