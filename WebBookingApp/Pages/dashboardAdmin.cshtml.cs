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
    public class dashboardAdminModel : PageModel
    {
        private readonly ILogger<dashboardAdminModel> _logger;
        private readonly string connString = "Server=MELON\\SQL2022;Database=BOOKING_DB;Trusted_Connection=True;";

        public string FirstName { get; private set; } = ""; // Display username as FirstName
        public byte[]? ProfilePicture { get; private set; }

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public dashboardAdminModel(ILogger<dashboardAdminModel> logger) => _logger = logger;

        public void OnGet()
        {
            _logger.LogInformation("OnGet() started");

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

            // If the user is admin, use "admin" as the identifier (not email)
            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin"; // Admin doesn't have a traditional email, so use "admin"
            }

            using SqlConnection conn = new(connString);
            conn.Open();

            // 🔹 Retrieve username for admin (from userAdmin table)
            string query = @"
        SELECT username 
        FROM dbo.userAdmin 
        WHERE username = @Username"; // Assuming 'username' is the unique field for admins

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Username", userEmail); // Use username, not email
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["username"].ToString();  // Displaying username
                    _logger.LogInformation($"Retrieved Username: {FirstName}");
                }
                else
                {
                    _logger.LogWarning("No admin found with the specified username.");
                }
            }

            // 🔹 Retrieve profile picture for admin from userPictures table
            query = "SELECT Picture FROM dbo.userPictures WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail); // Use email field for non-admin users
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

            // If the user is admin, use "admin" as email
            if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
            {
                userEmail = "admin"; // Admin does not have a traditional email, so use "admin"
            }

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

                // If the user is admin, use "admin" as email for storage
                if (string.IsNullOrEmpty(userEmail) || User.IsInRole("Admin"))
                {
                    userEmail = "admin"; // Admin uses a static email 'admin' for profile picture
                }

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

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    cmd.Parameters.AddWithValue("@ImageData", fileData);
                    await cmd.ExecuteNonQueryAsync();
                }

                _logger.LogInformation("Profile picture uploaded successfully.");

                return RedirectToPage("/dashboardAdmin");
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
