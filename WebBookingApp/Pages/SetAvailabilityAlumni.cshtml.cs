using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;

namespace WebBookingApp.Pages
{
    public class SetAvailabilityAlumniModel : PageModel
    {
        private readonly ILogger<SetAvailabilityAlumniModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connString;

        public string FirstName { get; private set; } = "";
        public string LastName { get; private set; } = "";
        public string Role { get; set; } = "";
        public byte[]? ProfilePicture { get; private set; }
        public string StudentOrganizationName { get; private set; } = "N/A";
        public string StudentOrgRole { get; private set; } = "N/A";
        public List<AlumniAvailability> AvailabilityList { get; private set; } = new();

        [BindProperty] public IFormFile? UploadedFile { get; set; }
        public string Message { get; private set; } = "";

        public class AlumniAvailability
        {
            public int AvailabilityId { get; set; }
            public bool IsAvailable { get; set; }
            public DateTime? AvailableDate { get; set; }
            public TimeSpan? StartTime { get; set; }
            public TimeSpan? EndTime { get; set; }
            public int? Capacity { get; set; }
            public string? Notes { get; set; }
        }

        public SetAvailabilityAlumniModel(ILogger<SetAvailabilityAlumniModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connString = _configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> OnPostAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return Page();

            using SqlConnection conn = new(_connString);
            await conn.OpenAsync();

            string alumniId = "";
            string getIdQuery = "SELECT alumni_id FROM dbo.userAlumni WHERE email = @Email";
            using (SqlCommand cmd = new(getIdQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                if (reader.Read())
                {
                    alumniId = reader["alumni_id"].ToString();
                }
            }

            if (string.IsNullOrEmpty(alumniId)) return Page();

            var isAvailable = Request.Form["status"].ToString() == "available";
            var availableDateStr = Request.Form["available_date"].ToString();
            var startTimeStr = Request.Form["start_time"].ToString();
            var endTimeStr = Request.Form["end_time"].ToString();
            var capacityStr = Request.Form["capacity"].ToString();
            var notesStr = Request.Form["notes"].ToString();

            string insertQuery = @"
                INSERT INTO dbo.AlumniAvailability
                (alumni_id, is_available, available_date, start_time, end_time, capacity, notes)
                VALUES
                (@AlumniId, @IsAvailable, @AvailableDate, @StartTime, @EndTime, @Capacity, @Notes)";

            using (SqlCommand cmd = new(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@AlumniId", alumniId);
                cmd.Parameters.AddWithValue("@IsAvailable", isAvailable);
                cmd.Parameters.AddWithValue("@AvailableDate", string.IsNullOrEmpty(availableDateStr) ? DBNull.Value : DateTime.Parse(availableDateStr));
                cmd.Parameters.AddWithValue("@StartTime", string.IsNullOrEmpty(startTimeStr) ? DBNull.Value : TimeSpan.Parse(startTimeStr));
                cmd.Parameters.AddWithValue("@EndTime", string.IsNullOrEmpty(endTimeStr) ? DBNull.Value : TimeSpan.Parse(endTimeStr));
                cmd.Parameters.AddWithValue("@Capacity", string.IsNullOrEmpty(capacityStr) ? DBNull.Value : int.Parse(capacityStr));
                cmd.Parameters.AddWithValue("@Notes", string.IsNullOrEmpty(notesStr) ? DBNull.Value : notesStr);

                await cmd.ExecuteNonQueryAsync();
            }

            return RedirectToPage();
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

            using SqlConnection conn = new(_connString);
            conn.Open();

            string query = @"
                SELECT us.first_name, us.last_name, us.role, org.OrganizationName, om.Role AS OrgRole
                FROM dbo.userStudents us
                LEFT JOIN dbo.OrganizationMembers om ON us.student_id = om.StudentId
                LEFT JOIN dbo.StudentOrganizations org ON om.OrganizationID = org.OrganizationID
                WHERE us.email = @Email
                UNION
                SELECT ua.first_name, ua.last_name, ua.role, NULL, NULL
                FROM dbo.userAlumni ua
                WHERE ua.email = @Email";

            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    FirstName = reader["first_name"].ToString();
                    LastName = reader["last_name"].ToString();
                    Role = reader["role"].ToString();
                    StudentOrganizationName = reader["OrganizationName"]?.ToString() ?? "N/A";
                    StudentOrgRole = reader["OrgRole"]?.ToString() ?? "N/A";
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

            string alumniId = null;
            query = "SELECT CAST(alumni_id AS NVARCHAR) AS id FROM dbo.userAlumni WHERE email = @Email";
            using (SqlCommand cmd = new(query, conn))
            {
                cmd.Parameters.AddWithValue("@Email", userEmail);
                using SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    alumniId = reader["id"]?.ToString();
                }
            }

            if (!string.IsNullOrEmpty(alumniId))
            {
                query = @"
                    SELECT availability_id, is_available, available_date, start_time, end_time, capacity, notes
                    FROM dbo.AlumniAvailability
                    WHERE alumni_id = @AlumniId
                    ORDER BY available_date DESC";

                using (SqlCommand cmd = new(query, conn))
                {
                    cmd.Parameters.AddWithValue("@AlumniId", alumniId);
                    using SqlDataReader reader = cmd.ExecuteReader();
                    AvailabilityList = new List<AlumniAvailability>();
                    while (reader.Read())
                    {
                        AvailabilityList.Add(new AlumniAvailability
                        {
                            AvailabilityId = Convert.ToInt32(reader["availability_id"]),
                            IsAvailable = Convert.ToBoolean(reader["is_available"]),
                            AvailableDate = reader["available_date"] as DateTime?,
                            StartTime = reader["start_time"] as TimeSpan?,
                            EndTime = reader["end_time"] as TimeSpan?,
                            Capacity = reader["capacity"] as int?,
                            Notes = reader["notes"]?.ToString()
                        });
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAvailabilityAsync()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return RedirectToPage("/Login");

            try
            {
                if (!int.TryParse(Request.Form["AvailabilityId"], out int availabilityId))
                {
                    _logger.LogWarning("Invalid AvailabilityId provided.");
                    return RedirectToPage("/SetAvailabilityAlumni");
                }

                using SqlConnection conn = new(_connString);
                await conn.OpenAsync();

                // Optional: verify ownership (recommended in production)
                string verifyQuery = @"
            DELETE FROM dbo.AlumniAvailability
            WHERE availability_id = @AvailabilityId
            AND alumni_id = (SELECT alumni_id FROM dbo.userAlumni WHERE email = @Email)";

                using (SqlCommand cmd = new(verifyQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@AvailabilityId", availabilityId);
                    cmd.Parameters.AddWithValue("@Email", userEmail);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync();

                    _logger.LogInformation($"Deleted {rowsAffected} row(s) for AvailabilityId {availabilityId}.");
                }

                return RedirectToPage("/SetAvailabilityAlumni");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting availability: {ex.Message}");
                return RedirectToPage("/SetAvailabilityAlumni");
            }
        }


        public IActionResult OnGetProfilePicture()
        {
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail)) return NotFound();

            using SqlConnection conn = new(_connString);
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
                return Page();
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLower();
            if (!allowedExtensions.Contains(fileExtension))
            {
                Message = "Only image files (.jpg, .png, .gif) are allowed.";
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

                using SqlConnection conn = new(_connString);
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

                return RedirectToPage("/dashboardStudent");
            }
            catch
            {
                Message = "Error uploading file.";
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
